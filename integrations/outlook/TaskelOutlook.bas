Attribute VB_Name = "TaskelOutlook"
Option Explicit

' Taskel for Outlook (classic Outlook for Windows)
'
' Sends the selected emails to Taskel, where each one becomes a task.
' See README.md next to this file for installation steps.
'
' Macros:
'   SendToTaskel     - create a task from each selected (or open) email
'   ConfigureTaskel  - set the Taskel address and API token
'
' This file is plain ASCII on purpose: the VBA editor imports .bas files
' using the system code page.

Private Const APP_NAME As String = "Taskel"
Private Const SETTINGS_SECTION As String = "Outlook"
Private Const DEFAULT_BASE_URL As String = "http://localhost:5000"
Private Const MAX_SUBJECT_CHARS As Long = 1000
Private Const MAX_SENDER_CHARS As Long = 320
Private Const MAX_BODY_CHARS As Long = 4000
Private Const MAX_MESSAGE_ID_CHARS As Long = 998
Private Const OL_MAIL As Long = 43
Private Const PR_INTERNET_MESSAGE_ID As String = "http://schemas.microsoft.com/mapi/proptag/0x1035001F"

' ---------------------------------------------------------------------------
' Public macros
' ---------------------------------------------------------------------------

Public Sub SendToTaskel()
    Dim mails As Collection
    Set mails = SelectedMails()
    If mails.Count = 0 Then
        MsgBox "Select or open at least one email first.", vbInformation, APP_NAME
        Exit Sub
    End If

    If Len(SavedToken()) = 0 Then
        ConfigureTaskel
        If Len(SavedToken()) = 0 Then Exit Sub
    End If

    Dim added As Long
    Dim i As Long
    Dim status As Long
    Dim detail As String

    For i = 1 To mails.Count
        If PostEmail(mails(i), status, detail) Then
            added = added + 1
        Else
            Exit For
        End If
    Next i

    If added = mails.Count Then
        MsgBox TaskCountText(added) & " added to Taskel.", vbInformation, APP_NAME
    Else
        MsgBox FailureText(status, detail) & vbCrLf & vbCrLf & _
            TaskCountText(added) & " added before the problem (" & mails.Count & " selected).", _
            vbExclamation, APP_NAME
    End If
End Sub

Public Sub ConfigureTaskel()
    Dim baseUrl As String
    Dim token As String

    baseUrl = SavedBaseUrl()
    baseUrl = Trim$(InputBox("Address of your Taskel API:", APP_NAME, baseUrl))
    If Len(baseUrl) = 0 Then Exit Sub
    Do While Right$(baseUrl, 1) = "/"
        baseUrl = Left$(baseUrl, Len(baseUrl) - 1)
    Loop

    token = Trim$(InputBox( _
        "Paste your Taskel API token (it starts with tsk_)." & vbCrLf & vbCrLf & _
        "Create one in Taskel: Integrari > Token nou.", APP_NAME))
    If Len(token) = 0 Then Exit Sub

    SaveSetting APP_NAME, SETTINGS_SECTION, "BaseUrl", baseUrl
    SaveSetting APP_NAME, SETTINGS_SECTION, "Token", token

    Dim status As Long
    Dim detail As String
    If SendRequest("GET", "/api/tasks", "", status, detail) And status = 200 Then
        MsgBox "Connected to Taskel.", vbInformation, APP_NAME
    Else
        MsgBox "Settings saved, but the connection test failed." & vbCrLf & vbCrLf & _
            FailureText(status, detail), vbExclamation, APP_NAME
    End If
End Sub

' ---------------------------------------------------------------------------
' Outlook selection
' ---------------------------------------------------------------------------

' The open email when a message window is active, otherwise the emails
' selected in the list. Meeting requests, contacts and so on are skipped.
Private Function SelectedMails() As Collection
    Dim result As New Collection
    Dim activeWin As Object
    Dim picked As Object
    Dim entry As Object
    Dim i As Long

    Set SelectedMails = result

    ' Some views (for example Outlook Today) have no selection; treat any
    ' failure here as "nothing selected".
    On Error GoTo Done
    Set activeWin = Application.ActiveWindow
    If activeWin Is Nothing Then Exit Function

    If TypeName(activeWin) = "Inspector" Then
        Set entry = activeWin.CurrentItem
        If IsMail(entry) Then result.Add entry
    Else
        Set picked = activeWin.Selection
        For i = 1 To picked.Count
            Set entry = picked.Item(i)
            If IsMail(entry) Then result.Add entry
        Next i
    End If

Done:
End Function

Private Function IsMail(ByVal entry As Object) As Boolean
    On Error GoTo NotMail
    If entry Is Nothing Then Exit Function
    IsMail = (entry.Class = OL_MAIL)
    Exit Function

NotMail:
    IsMail = False
End Function

' ---------------------------------------------------------------------------
' Taskel API
' ---------------------------------------------------------------------------

Private Function PostEmail(ByVal mail As Object, ByRef status As Long, ByRef detail As String) As Boolean
    Dim payload As String

    On Error GoTo BuildFailed
    payload = EmailJson(mail)
    On Error GoTo 0

    If SendRequest("POST", "/api/tasks/from-email", payload, status, detail) Then
        PostEmail = (status = 201)
    End If
    Exit Function

BuildFailed:
    status = -1
    detail = "Could not read the email: " & Err.Description
End Function

' Returns True when the server answered (whatever the status code), False
' when it could not be reached. In that case status is 0.
Private Function SendRequest(ByVal method As String, ByVal path As String, ByVal body As String, _
                             ByRef status As Long, ByRef detail As String) As Boolean
    Dim http As Object

    status = 0
    detail = ""

    On Error GoTo RequestFailed
    Set http = CreateObject("WinHttp.WinHttpRequest.5.1")
    http.SetTimeouts 5000, 5000, 15000, 15000
    http.Open method, SavedBaseUrl() & path, False
    http.SetRequestHeader "Authorization", "Bearer " & SavedToken()
    http.SetRequestHeader "Accept", "application/json"
    If Len(body) > 0 Then
        http.SetRequestHeader "Content-Type", "application/json; charset=utf-8"
        http.Send body
    Else
        http.Send
    End If

    status = http.Status
    If status < 200 Or status > 299 Then detail = Left$(http.ResponseText, 300)
    SendRequest = True
    Exit Function

RequestFailed:
    status = 0
    detail = Err.Description
End Function

Private Function EmailJson(ByVal mail As Object) As String
    Dim json As String

    json = "{""subject"":" & JsonString(Left$(mail.Subject, MAX_SUBJECT_CHARS))
    json = json & ",""sender"":" & JsonString(Left$(SenderText(mail), MAX_SENDER_CHARS))
    json = json & ",""senderEmail"":" & JsonString(Left$(SenderAddress(mail), MAX_SENDER_CHARS))
    json = json & ",""body"":" & JsonString(Left$(mail.Body, MAX_BODY_CHARS))
    json = json & ",""importance"":""" & ImportanceText(mail.Importance) & """"

    ' Unsent drafts report a placeholder date in the year 4501.
    If Year(mail.ReceivedTime) < 4000 Then
        json = json & ",""receivedAt"":""" & _
            Format$(mail.ReceivedTime, "yyyy\-mm\-dd\Thh\:nn\:ss") & """"
    End If

    ' Taskel replies to the sender when the task is completed. The Message-ID
    ' lets that reply land in the same conversation as this email.
    Dim messageId As String
    messageId = InternetMessageId(mail)
    If Len(messageId) > 0 And Len(messageId) <= MAX_MESSAGE_ID_CHARS Then
        json = json & ",""messageId"":" & JsonString(messageId)
    End If

    EmailJson = json & "}"
End Function

' The sender's SMTP address. Exchange senders are stored as an internal
' (X.500) address, which is resolved to the SMTP one where possible.
Private Function SenderAddress(ByVal mail As Object) As String
    Dim resolved As String

    SenderAddress = Trim$(mail.SenderEmailAddress)
    If mail.SenderEmailType <> "EX" Then Exit Function

    On Error GoTo KeepOriginal
    resolved = mail.Sender.GetExchangeUser.PrimarySmtpAddress
    If Len(resolved) > 0 Then SenderAddress = resolved
    Exit Function

KeepOriginal:
End Function

' The email's Message-ID header ("<...@...>"), or "" when Outlook has none.
Private Function InternetMessageId(ByVal mail As Object) As String
    On Error GoTo Missing
    InternetMessageId = Trim$(mail.PropertyAccessor.GetProperty(PR_INTERNET_MESSAGE_ID))
    Exit Function

Missing:
    InternetMessageId = ""
End Function

Private Function SenderText(ByVal mail As Object) As String
    Dim displayName As String
    Dim senderAddress As String

    displayName = Trim$(mail.SenderName)
    senderAddress = Trim$(mail.SenderEmailAddress)

    If Len(displayName) = 0 Or displayName = senderAddress Then
        SenderText = senderAddress
    ElseIf Len(senderAddress) = 0 Then
        SenderText = displayName
    Else
        SenderText = displayName & " <" & senderAddress & ">"
    End If
End Function

' Outlook: 0 = low, 1 = normal, 2 = high.
Private Function ImportanceText(ByVal importance As Long) As String
    Select Case importance
        Case 0: ImportanceText = "low"
        Case 2: ImportanceText = "high"
        Case Else: ImportanceText = "normal"
    End Select
End Function

' Quotes a string for JSON. Everything outside printable ASCII is written as
' \uXXXX, so the request body is pure ASCII and no text encoding can break
' diacritics on the way to the server.
Private Function JsonString(ByVal value As String) As String
    Dim parts() As String
    Dim i As Long
    Dim code As Long
    Dim ch As String

    ReDim parts(0 To Len(value) + 1)
    parts(0) = """"

    For i = 1 To Len(value)
        ch = Mid$(value, i, 1)
        code = AscW(ch) And &HFFFF&
        Select Case code
            Case 34: parts(i) = "\"""
            Case 92: parts(i) = "\\"
            Case 10: parts(i) = "\n"
            Case 13: parts(i) = "\r"
            Case 9: parts(i) = "\t"
            Case 32 To 126: parts(i) = ch
            Case Else: parts(i) = "\u" & Right$("0000" & Hex$(code), 4)
        End Select
    Next i

    parts(Len(value) + 1) = """"
    JsonString = Join(parts, "")
End Function

' ---------------------------------------------------------------------------
' Settings and messages
' ---------------------------------------------------------------------------

Private Function SavedBaseUrl() As String
    SavedBaseUrl = GetSetting(APP_NAME, SETTINGS_SECTION, "BaseUrl", DEFAULT_BASE_URL)
End Function

Private Function SavedToken() As String
    SavedToken = GetSetting(APP_NAME, SETTINGS_SECTION, "Token", "")
End Function

Private Function TaskCountText(ByVal taskCount As Long) As String
    If taskCount = 1 Then
        TaskCountText = "1 task"
    Else
        TaskCountText = taskCount & " tasks"
    End If
End Function

Private Function FailureText(ByVal status As Long, ByVal detail As String) As String
    Select Case status
        Case 0
            FailureText = "Could not reach Taskel at " & SavedBaseUrl() & "." & vbCrLf & _
                "Check that Taskel is running and the address is right (run ConfigureTaskel)." & _
                vbCrLf & vbCrLf & detail
        Case -1
            FailureText = detail
        Case 401
            FailureText = "Taskel rejected the API token. It may have been revoked." & vbCrLf & _
                "Create a new token in Taskel and run ConfigureTaskel."
        Case Else
            FailureText = "Taskel answered with an error (" & status & ")." & vbCrLf & vbCrLf & detail
    End Select
End Function
