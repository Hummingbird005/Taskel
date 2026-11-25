Attribute VB_Name = "TaskelOutlook"
Option Explicit

' Taskel for Outlook (classic Outlook for Windows)
'
' Sends the selected emails to Taskel, where each one becomes a task.
' See README.md next to this file for installation steps.
'
' Macros:
'   SendToTaskel     - create a task from each selected (or open) email,
'                      with the files attached to it. With the
'                      TaskelSendForm window installed, it first asks who
'                      gets the emails (one task for each), by when, and
'                      for instructions for them. If the window cannot
'                      open, it says why and asks before sending the
'                      emails as tasks of your own. Each email sent is then
'                      marked with the Outlook category "Taskel".
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
Private Const MAX_INSTRUCTIONS_CHARS As Long = 2000
Private Const OL_MAIL As Long = 43
Private Const PR_INTERNET_MESSAGE_ID As String = "http://schemas.microsoft.com/mapi/proptag/0x1035001F"
Private Const PR_ATTACH_CONTENT_ID As String = "http://schemas.microsoft.com/mapi/proptag/0x3712001F"
Private Const OL_BY_VALUE As Long = 1
Private Const OL_EMBEDDED_ITEM As Long = 5
Private Const OL_FORMAT_HTML As Long = 2
Private Const UPLOAD_TIMEOUT_MS As Long = 600000
' The Outlook category put on emails sent to Taskel, and its colour (olCategoryColorPurple).
Private Const TASKEL_CATEGORY As String = "Taskel"
Private Const TASKEL_CATEGORY_COLOR As Long = 9

' Who gets the emails during one run of SendToTaskel, as chosen in the
' TaskelSendForm window. Without the window (and only after asking), each email
' is a task of your own.
Private distributing As Boolean
Private recipientsJson As String
Private instructionsText As String
Private dueText As String
Private tasksCreated As Long
' Emails whose task was created but which Outlook would not let us mark.
Private unmarked As Long

' What happened to the attachments during one run of SendToTaskel.
Private attachmentsAdded As Long
Private attachmentProblems As String
Private maxAttachmentBytes As Double

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

    If Not AskRecipients(mails.Count) Then Exit Sub

    tasksCreated = 0
    unmarked = 0
    attachmentsAdded = 0
    attachmentProblems = ""
    maxAttachmentBytes = AttachmentLimitBytes()

    For i = 1 To mails.Count
        If PostEmail(mails(i), status, detail) Then
            added = added + 1
        Else
            Exit For
        End If
    Next i

    If added = mails.Count Then
        If Len(attachmentProblems) = 0 Then
            MsgBox TaskCountText(tasksCreated) & " added to Taskel." & AttachmentCountText() & UnmarkedText(), _
                vbInformation, APP_NAME
        Else
            MsgBox TaskCountText(tasksCreated) & " added to Taskel." & AttachmentCountText() & UnmarkedText() & _
                vbCrLf & vbCrLf & "Some attachments were not added:" & vbCrLf & attachmentProblems, vbExclamation, APP_NAME
        End If
    Else
        MsgBox FailureText(status, detail) & vbCrLf & vbCrLf & _
            added & " of " & mails.Count & " emails sent before the problem (" & _
            TaskCountText(tasksCreated) & " added).", vbExclamation, APP_NAME
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
' Who gets the emails
' ---------------------------------------------------------------------------

' Shows the TaskelSendForm window, once for all the selected emails, and
' keeps what was chosen there. Returns False when the user gave up.
' When the window cannot open (it is installed separately, and an error while
' it opens would otherwise go unnoticed), or Taskel is too old to list
' recipients, it says why and asks before sending the emails as tasks of
' your own: they are never sent that way without a word.
Private Function AskRecipients(ByVal mailCount As Long) As Boolean
    Dim form As Object
    Dim status As Long
    Dim detail As String
    Dim response As String
    Dim openError As String

    distributing = False
    recipientsJson = "[]"
    instructionsText = ""
    dueText = ""
    AskRecipients = True

    On Error Resume Next
    Set form = VBA.UserForms.Add("TaskelSendForm")
    If Err.Number <> 0 Then openError = Err.Description & " (error " & Err.Number & ")"
    On Error GoTo 0
    If form Is Nothing Then
        AskRecipients = SendAsOwnTasks( _
            "The window for choosing who gets the emails (TaskelSendForm) could not be opened:" & vbCrLf & _
            "    " & IIf(Len(openError) > 0, openError, "unknown error") & vbCrLf & vbCrLf & _
            "Check in the VBA editor (Alt+F11) that a UserForm whose (Name) is exactly TaskelSendForm " & _
            "is in the same project as this macro, and that its code is the whole TaskelSendForm.txt " & _
            "(delete the old code, paste the file again, then Debug > Compile).")
        Exit Function
    End If

    If Not SendRequest("GET", "/api/outlook/recipients", "", status, detail, response) Then
        MsgBox FailureText(status, detail), vbExclamation, APP_NAME
        AskRecipients = False
    ElseIf status = 404 Then
        ' Taskel before recipients could be chosen.
        AskRecipients = SendAsOwnTasks( _
            "This Taskel server cannot list the people to send emails to: it is an older version " & _
            "(or the address in ConfigureTaskel points to an older one).")
    ElseIf status <> 200 Then
        MsgBox FailureText(status, detail), vbExclamation, APP_NAME
        AskRecipients = False
    Else
        form.Prepare response, mailCount
        form.Show
        If form.Sent Then
            distributing = True
            recipientsJson = form.RecipientsJson
            instructionsText = SafeLeft(form.Instructions, MAX_INSTRUCTIONS_CHARS)
            ' A window pasted before due dates existed has no DueDate.
            On Error Resume Next
            dueText = form.DueDate
            On Error GoTo 0
            If Not dueText Like "####-##-##" Then dueText = ""
        Else
            AskRecipients = False
        End If
    End If

    Unload form
End Function

' Says why recipients cannot be chosen, and asks whether to send the emails
' as tasks of your own instead. True when the user agrees.
Private Function SendAsOwnTasks(ByVal reason As String) As Boolean
    SendAsOwnTasks = (MsgBox(reason & vbCrLf & vbCrLf & _
        "Send the selected emails to Taskel as tasks of your own, without choosing recipients?", _
        vbYesNo + vbExclamation + vbDefaultButton2, APP_NAME) = vbYes)
End Function

' ---------------------------------------------------------------------------
' Taskel API
' ---------------------------------------------------------------------------

Private Function PostEmail(ByVal mail As Object, ByRef status As Long, ByRef detail As String) As Boolean
    Dim payload As String
    Dim path As String
    Dim taskIds As Collection

    On Error GoTo BuildFailed
    payload = EmailJson(mail)
    On Error GoTo 0

    path = "/api/tasks/from-email"
    If distributing Then
        ' One task for each recipient, with the instructions; none means your own.
        payload = Left$(payload, Len(payload) - 1) & ",""recipients"":" & recipientsJson
        If Len(instructionsText) > 0 Then payload = payload & ",""instructions"":" & JsonString(instructionsText)
        If Len(dueText) > 0 Then payload = payload & ",""dueDate"":""" & dueText & """"
        payload = payload & "}"
        path = "/api/tasks/from-email/distribute"
    End If

    Dim response As String
    If SendRequest("POST", path, payload, status, detail, response) Then
        PostEmail = (status = 201)
    End If
    If Not PostEmail Then Exit Function

    If distributing Then
        Set taskIds = JsonStringArray(response, "taskIds")
    Else
        Set taskIds = New Collection
        taskIds.Add JsonValue(response, "id")
    End If
    tasksCreated = tasksCreated + taskIds.Count
    MarkAsSent mail

    ' The tasks exist from here on. A problem with an attachment is reported at
    ' the end and does not undo them.
    SendAttachments mail, taskIds
    Exit Function

BuildFailed:
    status = -1
    detail = "Could not read the email: " & Err.Description
End Function

' Returns True when the server answered (whatever the status code), False
' when it could not be reached. In that case status is 0.
Private Function SendRequest(ByVal method As String, ByVal path As String, ByVal body As String, _
                             ByRef status As Long, ByRef detail As String, _
                             Optional ByRef response As String) As Boolean
    Dim http As Object

    status = 0
    detail = ""
    response = ""

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
    If status < 200 Or status > 299 Then
        detail = Left$(http.ResponseText, 300)
    Else
        response = http.ResponseText
    End If
    SendRequest = True
    Exit Function

RequestFailed:
    status = 0
    detail = Err.Description
End Function

' ---------------------------------------------------------------------------
' The "Taskel" mark in Outlook
' ---------------------------------------------------------------------------

' Adds the category "Taskel" to an email whose task was created, so the list
' shows which emails are already in Taskel. The category is created the first
' time. An email Outlook will not change (in a read-only shared mailbox, for
' example) keeps its task and is only counted, for the final message.
Private Sub MarkAsSent(ByVal mail As Object)
    Dim current As String

    On Error GoTo NotMarked
    EnsureTaskelCategory
    current = mail.Categories
    If HasCategory(current, TASKEL_CATEGORY) Then Exit Sub
    If Len(Trim$(current)) = 0 Then
        mail.Categories = TASKEL_CATEGORY
    Else
        mail.Categories = current & ListSeparator() & " " & TASKEL_CATEGORY
    End If
    mail.Save
    Exit Sub

NotMarked:
    unmarked = unmarked + 1
End Sub

Private Sub EnsureTaskelCategory()
    Dim found As Object

    On Error Resume Next
    Set found = Application.Session.Categories.Item(TASKEL_CATEGORY)
    On Error GoTo 0
    If found Is Nothing Then Application.Session.Categories.Add TASKEL_CATEGORY, TASKEL_CATEGORY_COLOR
End Sub

' Outlook keeps an item's categories as one text, separated by the list
' separator of the Windows region settings (a comma or a semicolon).
Private Function HasCategory(ByVal categories As String, ByVal categoryName As String) As Boolean
    Dim parts() As String
    Dim i As Long

    parts = Split(Replace(categories, ";", ","), ",")
    For i = 0 To UBound(parts)
        If StrComp(Trim$(parts(i)), categoryName, vbTextCompare) = 0 Then
            HasCategory = True
            Exit Function
        End If
    Next i
End Function

Private Function ListSeparator() As String
    On Error Resume Next
    ListSeparator = CreateObject("WScript.Shell").RegRead("HKCU\Control Panel\International\sList")
    On Error GoTo 0
    If Len(ListSeparator) = 0 Then ListSeparator = ","
End Function

Private Function UnmarkedText() As String
    If unmarked = 1 Then
        UnmarkedText = vbCrLf & "1 email could not be marked with the Taskel category in Outlook."
    ElseIf unmarked > 1 Then
        UnmarkedText = vbCrLf & unmarked & " emails could not be marked with the Taskel category in Outlook."
    End If
End Function

' ---------------------------------------------------------------------------
' Attachments
' ---------------------------------------------------------------------------

' Uploads the files attached to an email to each task made from it. Each
' file is saved once and sent to every task.
' Pictures shown inside the message (logos in signatures and the like) are
' left out: they are part of how the email looks, not files someone sent.
Private Sub SendAttachments(ByVal mail As Object, ByVal taskIds As Collection)
    Dim item As Object
    Dim fileName As String
    Dim tempPath As String
    Dim status As Long
    Dim detail As String
    Dim taskId As Variant
    Dim stored As Boolean
    Dim problem As String

    If taskIds.Count = 0 Then Exit Sub

    On Error GoTo ListFailed
    If mail.Attachments.Count = 0 Then Exit Sub
    On Error GoTo 0

    For Each item In mail.Attachments
        fileName = ""
        tempPath = ""
        On Error GoTo ItemFailed

        If item.Type <> OL_BY_VALUE And item.Type <> OL_EMBEDDED_ITEM Then GoTo NextItem
        If IsInlinePicture(mail, item) Then GoTo NextItem

        fileName = AttachmentName(item)
        If maxAttachmentBytes > 0 And item.Size > maxAttachmentBytes Then
            NoteAttachmentProblem fileName, "larger than the limit of " & _
                Format$(maxAttachmentBytes / 1048576#, "0") & " MB"
            GoTo NextItem
        End If

        tempPath = TempFilePath()
        item.SaveAsFile tempPath
        If FileLen(tempPath) = 0 Then
            NoteAttachmentProblem fileName, "the file is empty"
        Else
            stored = False
            problem = ""
            For Each taskId In taskIds
                If UploadFile(CStr(taskId), tempPath, fileName, status, detail) Then
                    stored = True
                ElseIf Len(problem) = 0 Then
                    problem = UploadFailureText(status, detail)
                End If
            Next taskId
            If stored Then attachmentsAdded = attachmentsAdded + 1
            If Len(problem) > 0 Then
                If taskIds.Count > 1 Then problem = problem & " (for some recipients)"
                NoteAttachmentProblem fileName, problem
            End If
        End If

NextItem:
        On Error Resume Next
        If Len(tempPath) > 0 Then Kill tempPath
        On Error GoTo 0
    Next item
    Exit Sub

ItemFailed:
    If Len(fileName) = 0 Then fileName = "(attachment)"
    NoteAttachmentProblem fileName, Err.Description
    Resume NextItem

ListFailed:
    NoteAttachmentProblem "(attachments)", Err.Description
End Sub

' True for a picture the message displays in its body rather than offers as a file.
Private Function IsInlinePicture(ByVal mail As Object, ByVal item As Object) As Boolean
    Dim contentId As String

    On Error GoTo NotInline
    If mail.BodyFormat <> OL_FORMAT_HTML Then Exit Function
    contentId = Trim$(item.PropertyAccessor.GetProperty(PR_ATTACH_CONTENT_ID))
    If Len(contentId) = 0 Then Exit Function
    IsInlinePicture = (InStr(1, mail.HTMLBody, "cid:" & contentId, vbTextCompare) > 0)
    Exit Function

NotInline:
    IsInlinePicture = False
End Function

Private Function AttachmentName(ByVal item As Object) As String
    On Error Resume Next
    AttachmentName = Trim$(item.FileName)
    If Len(AttachmentName) = 0 Then AttachmentName = Trim$(item.DisplayName)
    If Len(AttachmentName) = 0 Then AttachmentName = "attachment"
    ' An attached email has no extension of its own; it is saved as a message file.
    If item.Type = OL_EMBEDDED_ITEM And InStr(AttachmentName, ".") = 0 Then
        AttachmentName = AttachmentName & ".msg"
    End If
    On Error GoTo 0
End Function

Private Function TempFilePath() As String
    Randomize
    TempFilePath = Environ$("TEMP") & "\taskel-" & Format$(Now, "yyyymmddhhnnss") & "-" & _
        CStr(Int(Rnd() * 1000000)) & ".tmp"
End Function

' Sends one file as multipart/form-data, the way a browser form would.
' Returns True when Taskel stored it.
Private Function UploadFile(ByVal taskId As String, ByVal filePath As String, ByVal fileName As String, _
                            ByRef status As Long, ByRef detail As String) As Boolean
    Dim boundary As String
    Dim quote As String
    Dim body As Object
    Dim fileData As Object
    Dim http As Object

    status = 0
    detail = ""
    quote = Chr$(34)
    Randomize
    boundary = "----TaskelBoundary" & Format$(Now, "yyyymmddhhnnss") & CStr(Int(Rnd() * 1000000))

    On Error GoTo UploadFailed
    Set body = CreateObject("ADODB.Stream")
    body.Type = 1
    body.Open
    body.Write Utf8Bytes("--" & boundary & vbCrLf & _
        "Content-Disposition: form-data; name=" & quote & "file" & quote & _
        "; filename=" & quote & HeaderFileName(fileName) & quote & vbCrLf & _
        "Content-Type: application/octet-stream" & vbCrLf & vbCrLf)

    Set fileData = CreateObject("ADODB.Stream")
    fileData.Type = 1
    fileData.Open
    fileData.LoadFromFile filePath
    body.Write fileData.Read
    fileData.Close

    body.Write Utf8Bytes(vbCrLf & "--" & boundary & "--" & vbCrLf)
    body.Position = 0

    Set http = CreateObject("WinHttp.WinHttpRequest.5.1")
    http.SetTimeouts 5000, 5000, UPLOAD_TIMEOUT_MS, UPLOAD_TIMEOUT_MS
    http.Open "POST", SavedBaseUrl() & "/api/tasks/" & taskId & "/attachments", False
    http.SetRequestHeader "Authorization", "Bearer " & SavedToken()
    http.SetRequestHeader "Accept", "application/json"
    http.SetRequestHeader "Content-Type", "multipart/form-data; boundary=" & boundary
    http.Send body.Read
    body.Close

    status = http.Status
    If status = 201 Then
        UploadFile = True
    Else
        detail = Left$(http.ResponseText, 300)
    End If
    Exit Function

UploadFailed:
    status = 0
    detail = Err.Description
End Function

' The bytes of a text in UTF-8, without the byte order mark ADODB puts first.
Private Function Utf8Bytes(ByVal value As String) As Variant
    Dim stream As Object

    Set stream = CreateObject("ADODB.Stream")
    stream.Type = 2
    stream.Charset = "utf-8"
    stream.Open
    stream.WriteText value
    stream.Position = 0
    stream.Type = 1
    stream.Position = 3
    Utf8Bytes = stream.Read
    stream.Close
End Function

' A file name that is safe inside the quotes of a header: no quotes,
' backslashes or line breaks. Taskel cleans the name again on its side.
Private Function HeaderFileName(ByVal fileName As String) As String
    Dim result As String

    result = Replace(fileName, Chr$(34), "'")
    result = Replace(result, "\", "_")
    result = Replace(result, vbCr, " ")
    result = Replace(result, vbLf, " ")
    HeaderFileName = Left$(result, 200)
End Function

' Asks Taskel for the largest file it accepts, so an oversized attachment is
' skipped without being sent. Returns 0 when Taskel did not say.
Private Function AttachmentLimitBytes() As Double
    Dim status As Long
    Dim detail As String
    Dim response As String

    If SendRequest("GET", "/api/attachments/limits", "", status, detail, response) Then
        If status = 200 Then AttachmentLimitBytes = Val(JsonValue(response, "maxFileSizeBytes"))
    End If
End Function

' Reads one simple value (a string or a number) from a JSON object by name.
' Enough for the small, flat answers this macro needs from Taskel.
Private Function JsonValue(ByVal json As String, ByVal propName As String) As String
    Dim quote As String
    Dim position As Long
    Dim finish As Long

    quote = Chr$(34)
    position = InStr(1, json, quote & propName & quote & ":", vbBinaryCompare)
    If position = 0 Then Exit Function
    position = position + Len(propName) + 3

    If Mid$(json, position, 1) = quote Then
        finish = InStr(position + 1, json, quote, vbBinaryCompare)
        If finish > 0 Then JsonValue = Mid$(json, position + 1, finish - position - 1)
    Else
        finish = position
        Do While finish <= Len(json)
            If InStr("0123456789.-", Mid$(json, finish, 1)) = 0 Then Exit Do
            finish = finish + 1
        Loop
        JsonValue = Mid$(json, position, finish - position)
    End If
End Function

' Reads an array of strings from a JSON object by name, such as the
' "taskIds" Taskel answers with. The values are ids: no quotes or commas
' inside them.
Private Function JsonStringArray(ByVal json As String, ByVal propName As String) As Collection
    Dim result As New Collection
    Dim quote As String
    Dim position As Long
    Dim finish As Long
    Dim parts() As String
    Dim i As Long
    Dim value As String

    Set JsonStringArray = result
    quote = Chr$(34)
    position = InStr(1, json, quote & propName & quote & ":[", vbBinaryCompare)
    If position = 0 Then Exit Function
    position = position + Len(propName) + 4
    finish = InStr(position, json, "]", vbBinaryCompare)
    If finish <= position Then Exit Function

    parts = Split(Mid$(json, position, finish - position), ",")
    For i = 0 To UBound(parts)
        value = Replace(Trim$(parts(i)), quote, "")
        If Len(value) > 0 Then result.Add value
    Next i
End Function

Private Sub NoteAttachmentProblem(ByVal fileName As String, ByVal reason As String)
    ' Keep the final message readable when many attachments fail.
    If Len(attachmentProblems) > 900 Then Exit Sub
    attachmentProblems = attachmentProblems & "- " & Left$(fileName, 80) & ": " & Left$(reason, 160) & vbCrLf
End Sub

Private Function UploadFailureText(ByVal status As Long, ByVal detail As String) As String
    Select Case status
        Case 0
            UploadFailureText = "could not be sent (" & detail & ")"
        Case 413
            UploadFailureText = "larger than Taskel accepts"
        Case 409
            UploadFailureText = "the task already has the most files it can hold"
        Case 401
            UploadFailureText = "the API token was rejected"
        Case Else
            UploadFailureText = "Taskel answered with an error (" & status & ")"
    End Select
End Function

Private Function AttachmentCountText() As String
    If attachmentsAdded = 1 Then
        AttachmentCountText = vbCrLf & "1 attachment added."
    ElseIf attachmentsAdded > 1 Then
        AttachmentCountText = vbCrLf & attachmentsAdded & " attachments added."
    End If
End Function

Private Function EmailJson(ByVal mail As Object) As String
    Dim json As String

    json = "{""subject"":" & JsonString(SafeLeft(mail.Subject, MAX_SUBJECT_CHARS))
    json = json & ",""sender"":" & JsonString(SafeLeft(SenderText(mail), MAX_SENDER_CHARS))
    json = json & ",""senderEmail"":" & JsonString(SafeLeft(SenderAddress(mail), MAX_SENDER_CHARS))
    json = json & ",""body"":" & JsonString(SafeLeft(mail.Body, MAX_BODY_CHARS))
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

' The sender as shown in the task's notes: "Name <address>". For Exchange
' senders the address is the SMTP one where it can be resolved; an internal
' (X.500) address that could not be resolved is left out, as it means nothing
' to a reader.
Private Function SenderText(ByVal mail As Object) As String
    Dim displayName As String
    Dim address As String

    displayName = Trim$(mail.SenderName)
    address = Trim$(SenderAddress(mail))
    If Left$(address, 1) = "/" And Len(displayName) > 0 Then address = ""

    If Len(displayName) = 0 Or displayName = address Then
        SenderText = address
    ElseIf Len(address) = 0 Then
        SenderText = displayName
    Else
        SenderText = displayName & " <" & address & ">"
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

' The first maxChars characters of value, without cutting a surrogate pair
' (an emoji, for example) in half: half a pair is not valid JSON and Taskel
' would refuse the whole email.
Private Function SafeLeft(ByVal value As String, ByVal maxChars As Long) As String
    Dim code As Long

    SafeLeft = Left$(value, maxChars)
    If Len(SafeLeft) = 0 Or Len(value) <= maxChars Then Exit Function

    code = AscW(Right$(SafeLeft, 1)) And &HFFFF&
    If code >= &HD800& And code <= &HDBFF& Then SafeLeft = Left$(SafeLeft, Len(SafeLeft) - 1)
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
