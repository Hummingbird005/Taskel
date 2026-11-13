ü
C/home/user/taskel/backend/tests/Taskel.Api.Tests/ValidationTests.csTaskel.Api.TestsQ
 Taskel.Api.Tests.ValidationTests-Invalid_Task_Is_Rejected_On_Create_And_UpdateA
 Taskel.Api.Tests.ValidationTestsValid_Text_Is_Kept_As_WrittenB
 Taskel.Api.Tests.ValidationTestsBlank_Notes_Are_Stored_As_NoneF
 Taskel.Api.Tests.ValidationTests"Registration_Rejects_Invalid_EmailO
 Taskel.Api.Tests.ValidationTests+Registration_Rejects_Weak_Or_Blank_Password]
 Taskel.Api.Tests.ValidationTests9Registration_Rejects_Too_Long_Password_And_Unknown_Fields`
 Taskel.Api.Tests.ValidationTests<Token_Name_And_Completion_Response_Reject_Control_CharactersQ
 Taskel.Api.Tests.ValidationTests-Email_Content_Is_Cleaned_Rather_Than_RejectedN
 Taskel.Api.Tests.ValidationTests*Email_Request_Is_Still_Validated_For_Shape?
 Taskel.Api.Tests.ValidationTestsMalformed_Ids_Are_Not_Found3
 Taskel.Api.Tests.ValidationTestsSafe_Text_CheckB
 Taskel.Api.Tests.ValidationTestsUnpaired_Surrogates_Are_UnsafeE
 Taskel.Api.Tests.ValidationTests!Clean_Removes_Only_What_Is_UnsafeK
 Taskel.Api.Tests.ValidationTests'Half_A_Surrogate_Pair_Is_Not_Valid_Json†
E/home/user/taskel/backend/tests/Taskel.Api.Tests/UnderPostingTests.csTaskel.Api.Testsa
"Taskel.Api.Tests.UnderPostingTests;Changing_A_Department_Without_A_Role_Does_Not_Demote_AnyoneX
"Taskel.Api.Tests.UnderPostingTests2Saving_A_Task_Without_Its_State_Does_Not_Reopen_It\
"Taskel.Api.Tests.UnderPostingTests6An_Empty_Notification_Setting_Does_Not_Turn_Emails_OffL
"Taskel.Api.Tests.UnderPostingTests&An_Empty_Attachment_Setting_Is_RefusedB
"Taskel.Api.Tests.UnderPostingTestsComplete_Requests_Still_WorkÃ	
=/home/user/taskel/backend/tests/Taskel.Api.Tests/TeamTests.csTaskel.Api.TestsL
Taskel.Api.Tests.TeamTests.New_Account_Is_A_Plain_User_Without_DepartmentI
Taskel.Api.Tests.TeamTests+Plain_User_Cannot_See_Team_Or_Admin_ScreensK
Taskel.Api.Tests.TeamTests-Manager_Sees_The_Workload_Of_Their_DepartmentZ
Taskel.Api.Tests.TeamTests<Manager_Reads_A_Members_Tasks_In_Full_But_Cannot_Change_ThemI
Taskel.Api.Tests.TeamTests+Manager_Cannot_Look_Into_Another_DepartmentF
Taskel.Api.Tests.TeamTests(Manager_Without_A_Department_Sees_NobodyF
Taskel.Api.Tests.TeamTests(Administrator_Can_Look_At_Any_DepartmentO
Taskel.Api.Tests.TeamTests1Role_Changes_Take_Effect_Without_Signing_In_Again7
Taskel.Api.Tests.TeamTestsInvalid_Query_Is_RejectedD
Taskel.Api.Tests.TeamTests&A_Date_Far_From_The_Servers_Is_IgnoredF
Taskel.Api.Tests.TeamTests(Administrator_Sets_Roles_And_Departments=
Taskel.Api.Tests.TeamTestsInvalid_User_Update_Is_RejectedQ
Taskel.Api.Tests.TeamTests3Configured_Administrator_Is_Always_An_AdministratorQ
Taskel.Api.Tests.TeamTests3Department_And_Manager_Role_Come_From_The_DirectoryV
Taskel.Api.Tests.TeamTests8What_The_Directory_Does_Not_Say_Is_Left_As_Set_In_TaskelË
A/home/user/taskel/backend/tests/Taskel.Api.Tests/TasksApiTests.csTaskel.Api.TestsH
Taskel.Api.Tests.TasksApiTests&Health_ReturnsOk_WithoutAuthentication=
Taskel.Api.Tests.TasksApiTestsTasks_RequireAuthentication6
Taskel.Api.Tests.TasksApiTestsTask_Crud_Flow_WorksI
Taskel.Api.Tests.TasksApiTests'Create_WithEmptyTitle_ReturnsBadRequestF
Taskel.Api.Tests.TasksApiTests$Created_Task_Is_Persisted_And_ListedP
Taskel.Api.Tests.TasksApiTests.Update_And_Delete_Unknown_Task_Return_NotFoundN
Taskel.Api.Tests.TasksApiTests,Users_Cannot_See_Or_Change_Each_Others_TasksÈ
B/home/user/taskel/backend/tests/Taskel.Api.Tests/TaskStateTests.csTaskel.Api.Tests5
Taskel.Api.Tests.TaskStateTestsA_New_Task_Is_OpenM
Taskel.Api.Tests.TaskStateTests*A_Task_Goes_On_Hold_With_A_Reason_And_Back9
Taskel.Api.Tests.TaskStateTestsThe_Reason_Is_Requiredg
Taskel.Api.Tests.TaskStateTestsDCompleting_A_Task_On_Hold_Takes_It_Off_Hold_And_Keeps_The_ResolutionZ
Taskel.Api.Tests.TaskStateTests7A_Completed_Task_Cannot_Go_On_Hold_Until_It_Is_ReopenedT
Taskel.Api.Tests.TaskStateTests1Completing_Through_An_Edit_Also_Takes_It_Off_HoldJ
Taskel.Api.Tests.TaskStateTests'Editing_A_Task_On_Hold_Keeps_It_On_Hold{
Taskel.Api.Tests.TaskStateTestsXOnly_The_People_Working_On_It_Change_Its_State_And_Everyone_Who_Sees_It_Reads_The_ReasonK
Taskel.Api.Tests.TaskStateTests(A_Task_On_Hold_Still_Counts_As_Open_Workë
F/home/user/taskel/backend/tests/Taskel.Api.Tests/RealDirectoryTests.csTaskel.Api.Tests^
#Taskel.Api.Tests.RealDirectoryTests7Correct_Password_Returns_The_Account_From_The_DirectoryQ
#Taskel.Api.Tests.RealDirectoryTests*All_User_Name_Forms_Reach_The_Same_Accounta
#Taskel.Api.Tests.RealDirectoryTests:Wrong_Password_Unknown_User_And_Empty_Password_Are_RefusedB
#Taskel.Api.Tests.RealDirectoryTestsDisabled_Account_Is_Refused[
#Taskel.Api.Tests.RealDirectoryTests4Account_Without_Mail_Falls_Back_To_Its_Logon_AddressZ
#Taskel.Api.Tests.RealDirectoryTests3Required_Group_Lets_Members_In_And_Keeps_Others_Out]
#Taskel.Api.Tests.RealDirectoryTests6Department_And_Role_Groups_Are_Read_From_The_DirectoryT
#Taskel.Api.Tests.RealDirectoryTests-Unencrypted_Connection_Is_Not_Used_By_DefaultG
#Taskel.Api.Tests.RealDirectoryTests StartTls_Works_On_The_Plain_Portç
D/home/user/taskel/backend/tests/Taskel.Api.Tests/SmtpMessageTests.csTaskel.Api.TestsK
!Taskel.Api.Tests.SmtpMessageTests&Reply_Threads_Under_The_Original_EmailP
!Taskel.Api.Tests.SmtpMessageTests+Subject_Gets_A_Single_Re_Prefix_On_One_LineH
!Taskel.Api.Tests.SmtpMessageTests#From_Address_Overrides_The_UsernameR
!Taskel.Api.Tests.SmtpMessageTests-Is_Configured_Needs_A_Host_And_A_From_AddressP
!Taskel.Api.Tests.SmtpMessageTests+Sending_Without_Configuration_Fails_Clearlyä
G/home/user/taskel/backend/tests/Taskel.Api.Tests/TaskDetailsApiTests.csTaskel.Api.TestsL
$Taskel.Api.Tests.TaskDetailsApiTests$Create_Without_Details_Uses_DefaultsI
$Taskel.Api.Tests.TaskDetailsApiTests!Create_With_Details_Persists_ThemU
$Taskel.Api.Tests.TaskDetailsApiTests-Wire_Format_Uses_String_Priority_And_Iso_DateS
$Taskel.Api.Tests.TaskDetailsApiTests+Low_Priority_Is_Not_Replaced_By_The_DefaultT
$Taskel.Api.Tests.TaskDetailsApiTests,Tags_Are_Trimmed_LowerCased_And_DeduplicatedJ
$Taskel.Api.Tests.TaskDetailsApiTests"Update_Replaces_And_Clears_DetailsI
$Taskel.Api.Tests.TaskDetailsApiTests!Invalid_Details_Return_BadRequestU
$Taskel.Api.Tests.TaskDetailsApiTests-Duplicate_Tags_Do_Not_Count_Towards_The_Limitù	
E/home/user/taskel/backend/tests/Taskel.Api.Tests/NotificationTests.csTaskel.Api.TestsY
"Taskel.Api.Tests.NotificationTests3Assignee_Is_Emailed_When_A_Task_Is_Created_For_Themf
"Taskel.Api.Tests.NotificationTests@An_Interval_Is_Shown_As_A_Period_And_Medium_Priority_Is_Left_OutP
"Taskel.Api.Tests.NotificationTests*A_Task_You_Keep_For_Yourself_Sends_NothingL
"Taskel.Api.Tests.NotificationTests&Reassigning_Emails_The_New_Person_OnceV
"Taskel.Api.Tests.NotificationTests0Forwarding_Emails_The_New_Assignee_With_The_NoteP
"Taskel.Api.Tests.NotificationTests*Forwarding_Back_To_The_Creator_Emails_Them]
"Taskel.Api.Tests.NotificationTests7A_Group_Task_Emails_Every_Member_Except_Whoever_Gave_ItN
"Taskel.Api.Tests.NotificationTests(Forwarding_To_A_Group_Emails_Its_MembersT
"Taskel.Api.Tests.NotificationTests.Someone_Who_Turned_Notifications_Off_Gets_Noneh
"Taskel.Api.Tests.NotificationTestsBA_Failing_Mail_Server_Does_Not_Fail_The_Assignment_Or_Later_Emails[
"Taskel.Api.Tests.NotificationTests5Notification_Settings_Need_A_Session_And_A_Valid_Bodys
"Taskel.Api.Tests.NotificationTestsMNotification_Message_Is_Marked_As_Automatic_And_Keeps_The_Subject_On_One_LineP
"Taskel.Api.Tests.NotificationTests*Without_A_Public_Url_The_Email_Has_No_LinkÑ
L/home/user/taskel/backend/tests/Taskel.Api.Tests/IntervalAndCalendarTests.csTaskel.Api.TestsF
)Taskel.Api.Tests.IntervalAndCalendarTestsTask_Can_Have_An_Interval^
)Taskel.Api.Tests.IntervalAndCalendarTests1Invalid_Interval_Is_Rejected_On_Create_And_UpdateS
)Taskel.Api.Tests.IntervalAndCalendarTests&Update_Changes_And_Clears_The_Interval_
)Taskel.Api.Tests.IntervalAndCalendarTests2Workload_Counts_Open_Tasks_That_Overlap_The_PeriodY
)Taskel.Api.Tests.IntervalAndCalendarTests,Workload_Can_Leave_Out_The_Task_Being_EditedJ
)Taskel.Api.Tests.IntervalAndCalendarTestsWorkload_Rejects_A_Bad_PeriodV
)Taskel.Api.Tests.IntervalAndCalendarTests)Workload_Needs_A_Known_User_And_A_Sessiong
)Taskel.Api.Tests.IntervalAndCalendarTests:Manager_Gets_The_Departments_Dated_Open_Tasks_For_A_Period]
)Taskel.Api.Tests.IntervalAndCalendarTests0Calendar_Is_For_Managers_Of_That_Department_OnlyJ
)Taskel.Api.Tests.IntervalAndCalendarTestsCalendar_Rejects_A_Bad_PeriodÑ
H/home/user/taskel/backend/tests/Taskel.Api.Tests/ForwardAndGroupTests.csTaskel.Api.TestsV
%Taskel.Api.Tests.ForwardAndGroupTests-Assignee_Forwards_A_Task_And_Keeps_Reading_ItP
%Taskel.Api.Tests.ForwardAndGroupTests'Forwarder_Can_No_Longer_Change_The_TaskT
%Taskel.Api.Tests.ForwardAndGroupTests+A_Task_Forwarded_Twice_Keeps_Its_Whole_PathP
%Taskel.Api.Tests.ForwardAndGroupTests'A_Task_Forwarded_Back_Is_Workable_Again[
%Taskel.Api.Tests.ForwardAndGroupTests2Forwarding_To_The_Creator_Returns_The_Task_To_ThemF
%Taskel.Api.Tests.ForwardAndGroupTestsOnly_The_Assignee_Can_ForwardR
%Taskel.Api.Tests.ForwardAndGroupTests)Forward_Needs_Exactly_One_Existing_Target^
%Taskel.Api.Tests.ForwardAndGroupTests5Group_Task_Is_Seen_By_Every_Member_Until_One_Takes_ItU
%Taskel.Api.Tests.ForwardAndGroupTests,A_Taken_Task_Can_Be_Handed_Back_To_The_Group]
%Taskel.Api.Tests.ForwardAndGroupTests4Claim_And_Release_Are_Refused_When_They_Do_Not_ApplyT
%Taskel.Api.Tests.ForwardAndGroupTests+A_Received_Task_Can_Be_Forwarded_To_A_Groupf
%Taskel.Api.Tests.ForwardAndGroupTests=Creator_Can_Move_A_Task_Between_A_Group_A_User_And_Themselves[
%Taskel.Api.Tests.ForwardAndGroupTests2An_Unclaimed_Group_Task_Counts_In_Nobodys_Workloadd
%Taskel.Api.Tests.ForwardAndGroupTests;Everyone_Sees_Groups_But_Only_Managers_And_Admins_Make_Them]
%Taskel.Api.Tests.ForwardAndGroupTests4Manager_Builds_Groups_From_Their_Own_Department_OnlyU
%Taskel.Api.Tests.ForwardAndGroupTests,Manager_Manages_Only_The_Groups_They_CreatedV
%Taskel.Api.Tests.ForwardAndGroupTests-Updating_A_Group_Changes_Its_Name_And_MembersA
%Taskel.Api.Tests.ForwardAndGroupTestsGroup_Input_Is_Validatedg
%Taskel.Api.Tests.ForwardAndGroupTests>Deleting_A_Group_Returns_Its_Unclaimed_Tasks_To_Their_Creators\
%Taskel.Api.Tests.ForwardAndGroupTests3Directory_Groups_Arrive_With_The_People_Who_Sign_Inc
%Taskel.Api.Tests.ForwardAndGroupTests:Leaving_A_Directory_Group_Takes_Effect_At_The_Next_Sign_Inm
%Taskel.Api.Tests.ForwardAndGroupTestsDDirectory_Groups_Cannot_Be_Edited_In_Taskel_Even_By_An_Administratorh
%Taskel.Api.Tests.ForwardAndGroupTests?When_The_Directory_Reports_No_Groups_Memberships_Are_Left_AloneW
%Taskel.Api.Tests.ForwardAndGroupTests.Group_Name_Is_Read_From_The_Distinguished_Nameb
%Taskel.Api.Tests.ForwardAndGroupTests9Only_Groups_Under_The_Configured_Container_Are_Brought_InÙ
B/home/user/taskel/backend/tests/Taskel.Api.Tests/EmailTaskTests.csTaskel.Api.Tests7
Taskel.Api.Tests.EmailTaskTestsEmail_Becomes_A_Task>
Taskel.Api.Tests.EmailTaskTestsImportance_Maps_To_PriorityK
Taskel.Api.Tests.EmailTaskTests(Missing_Subject_Gets_A_Placeholder_TitleM
Taskel.Api.Tests.EmailTaskTests*Long_Subject_And_Body_Are_Shortened_To_FitC
Taskel.Api.Tests.EmailTaskTests Invalid_Email_Returns_BadRequestX
Taskel.Api.Tests.EmailTaskTests5Requires_Authentication_And_Also_Works_With_A_SessionM
Taskel.Api.Tests.EmailTaskTests*Email_Task_Belongs_Only_To_The_Token_Ownerœ
G/home/user/taskel/backend/tests/Taskel.Api.Tests/DirectoryLoginTests.csTaskel.Api.Tests[
$Taskel.Api.Tests.DirectoryLoginTests3Options_Report_What_The_Sign_In_Screen_Should_Offer\
$Taskel.Api.Tests.DirectoryLoginTests4First_Sign_In_Creates_The_Account_From_The_Directoryi
$Taskel.Api.Tests.DirectoryLoginTestsASigning_In_Again_Reuses_The_Account_And_Follows_Directory_ChangesB
$Taskel.Api.Tests.DirectoryLoginTestsWrong_Password_Is_RejectedS
$Taskel.Api.Tests.DirectoryLoginTests+Unknown_User_And_Invalid_Input_Are_RejectedS
$Taskel.Api.Tests.DirectoryLoginTests+Directory_Outage_Is_Reported_As_Unavailable^
$Taskel.Api.Tests.DirectoryLoginTests6Directory_Account_Cannot_Sign_In_With_A_Local_Passwordk
$Taskel.Api.Tests.DirectoryLoginTestsCLocal_Account_With_The_Same_Email_Is_Taken_Over_And_Keeps_Its_TasksT
$Taskel.Api.Tests.DirectoryLoginTests,Two_Directory_Accounts_Cannot_Share_An_EmailI
$Taskel.Api.Tests.DirectoryLoginTests!Sign_In_Attempts_Are_Rate_Limited{
$Taskel.Api.Tests.DirectoryLoginTestsSDirectory_Sign_In_Is_Off_Unless_Configured_And_Local_Registration_Can_Be_Turned_OffA
$Taskel.Api.Tests.DirectoryLoginTestsUser_Names_Are_NormalisedI
$Taskel.Api.Tests.DirectoryLoginTests!Suspicious_User_Names_Are_RefusedA
$Taskel.Api.Tests.DirectoryLoginTestsFilter_Values_Are_Escaped
$Taskel.Api.Tests.DirectoryLoginTestsYReal_Authenticator_Refuses_Empty_Passwords_And_Bad_Names_Without_Contacting_The_Directoryk
$Taskel.Api.Tests.DirectoryLoginTestsCReal_Authenticator_Reports_An_Unreachable_Or_Unconfigured_Directory”
A/home/user/taskel/backend/tests/Taskel.Api.Tests/DemoDataTests.csTaskel.Api.Testsb
Taskel.Api.Tests.DemoDataTests@Every_Demo_Account_Signs_In_With_Its_Published_Password_And_RoleT
Taskel.Api.Tests.DemoDataTests2The_Sample_Tasks_Cover_Every_List_In_The_Interface[
Taskel.Api.Tests.DemoDataTests9Seeding_Leaves_A_Database_That_Already_Has_Accounts_Aloneb
Taskel.Api.Tests.DemoDataTests@A_Production_Server_Never_Gets_The_Demo_Accounts_Even_When_AskedB
Taskel.Api.Tests.DemoDataTests Demo_Passwords_Are_Stored_Hashed—
B/home/user/taskel/backend/tests/Taskel.Api.Tests/DashboardTests.csTaskel.Api.TestsV
Taskel.Api.Tests.DashboardTests3New_User_Has_The_Default_Layout_And_The_Reminder_On;
Taskel.Api.Tests.DashboardTestsLayout_Is_Saved_In_OrderU
Taskel.Api.Tests.DashboardTests2Empty_Layout_Is_Kept_And_Null_Restores_The_DefaultD
Taskel.Api.Tests.DashboardTests!Each_User_Has_Their_Own_DashboardQ
Taskel.Api.Tests.DashboardTests.Invalid_Layout_Is_Rejected_And_Nothing_Changes:
Taskel.Api.Tests.DashboardTestsRequires_AuthenticationÎ
H/home/user/taskel/backend/tests/Taskel.Api.Tests/CompletionReplyTests.csTaskel.Api.Tests_
%Taskel.Api.Tests.CompletionReplyTests6Completing_An_Email_Task_Sends_The_Response_As_A_ReplyZ
%Taskel.Api.Tests.CompletionReplyTests1Email_Task_Cannot_Be_Completed_Without_A_ResponseV
%Taskel.Api.Tests.CompletionReplyTests-Email_Task_Cannot_Be_Completed_Through_UpdateR
%Taskel.Api.Tests.CompletionReplyTests)Email_Task_Can_Still_Be_Edited_While_OpenS
%Taskel.Api.Tests.CompletionReplyTests*Response_Longer_Than_The_Limit_Is_RejectedZ
%Taskel.Api.Tests.CompletionReplyTests1When_The_Email_Cannot_Be_Sent_The_Task_Stays_OpenY
%Taskel.Api.Tests.CompletionReplyTests0When_Email_Is_Not_Configured_The_Task_Stays_OpenV
%Taskel.Api.Tests.CompletionReplyTests-Completing_Twice_Does_Not_Send_A_Second_Email]
%Taskel.Api.Tests.CompletionReplyTests4Reopened_Task_Needs_A_New_Response_To_Complete_Againc
%Taskel.Api.Tests.CompletionReplyTests:Manual_Task_Completes_Without_A_Response_And_Sends_Nothingk
%Taskel.Api.Tests.CompletionReplyTestsBEmail_Without_A_Usable_Sender_Address_Completes_Like_A_Manual_Taskp
%Taskel.Api.Tests.CompletionReplyTestsGSender_Address_Is_Taken_From_The_Display_Form_When_Not_Given_SeparatelyH
%Taskel.Api.Tests.CompletionReplyTestsMalformed_Message_Id_Is_Droppedc
%Taskel.Api.Tests.CompletionReplyTests:Another_User_Cannot_Complete_The_Task_Or_Trigger_The_EmailQ
%Taskel.Api.Tests.CompletionReplyTests(Reply_Headers_Are_Not_Exposed_In_The_ApiÊ
C/home/user/taskel/backend/tests/Taskel.Api.Tests/AssignmentTests.csTaskel.Api.TestsT
 Taskel.Api.Tests.AssignmentTests0New_Task_Belongs_To_Its_Creator_With_No_AssigneeY
 Taskel.Api.Tests.AssignmentTests5Assigned_Task_Is_Visible_To_Creator_And_Assignee_Only]
 Taskel.Api.Tests.AssignmentTests9Assignee_Can_Edit_And_Complete_But_Not_Delete_Or_ReassignL
 Taskel.Api.Tests.AssignmentTests(Creator_Can_Reassign_Unassign_And_DeleteL
 Taskel.Api.Tests.AssignmentTests(Assigning_To_An_Unknown_User_Is_RejectedG
 Taskel.Api.Tests.AssignmentTests#Update_Does_Not_Change_The_AssigneeQ
 Taskel.Api.Tests.AssignmentTests-Assignee_Completes_An_Email_Task_With_A_ReplyX
 Taskel.Api.Tests.AssignmentTests4Users_List_Shows_Names_And_Emails_But_Nothing_SecretO
 Taskel.Api.Tests.AssignmentTests+Task_Response_Does_Not_Leak_Internal_Fields„
C/home/user/taskel/backend/tests/Taskel.Api.Tests/AttachmentTests.csTaskel.Api.Testsb
 Taskel.Api.Tests.AttachmentTests>A_File_Is_Uploaded_Listed_On_The_Task_And_Downloaded_Unchanged^
 Taskel.Api.Tests.AttachmentTests:Downloads_Are_Never_Shown_By_The_Browser_As_Part_Of_TaskelJ
 Taskel.Api.Tests.AttachmentTests&File_Names_Are_Reduced_To_A_Plain_NameT
 Taskel.Api.Tests.AttachmentTests0Long_File_Names_Are_Cut_But_Keep_Their_Extension\
 Taskel.Api.Tests.AttachmentTests8The_Uploaded_Name_Never_Decides_Where_The_File_Is_StoredT
 Taskel.Api.Tests.AttachmentTests0Only_People_Who_Can_See_The_Task_Reach_Its_Filesc
 Taskel.Api.Tests.AttachmentTests?Assignee_Adds_And_Removes_Files_And_A_Forwarder_Only_Reads_Themf
 Taskel.Api.Tests.AttachmentTestsBGroup_Members_Read_Files_Before_Taking_The_Task_But_Cannot_Add_Any^
 Taskel.Api.Tests.AttachmentTests:A_File_Over_The_Limit_Is_Refused_And_Leaves_Nothing_Behindi
 Taskel.Api.Tests.AttachmentTestsEThe_Default_Limit_Is_100_Megabytes_And_An_Administrator_Can_Change_Ite
 Taskel.Api.Tests.AttachmentTestsAOnly_Administrators_Change_The_Limit_And_Only_To_A_Sensible_ValueJ
 Taskel.Api.Tests.AttachmentTests&A_Task_Holds_A_Limited_Number_Of_FilesA
 Taskel.Api.Tests.AttachmentTestsMalformed_Uploads_Are_RefusedT
 Taskel.Api.Tests.AttachmentTests0Removing_A_File_Or_Its_Task_Removes_It_From_DiskV
 Taskel.Api.Tests.AttachmentTests2A_Manager_Downloads_The_Files_Of_Their_People_OnlyU
 Taskel.Api.Tests.AttachmentTests1The_Outlook_Macro_Can_Attach_Files_With_Its_Token½
@/home/user/taskel/backend/tests/Taskel.Api.Tests/AuthApiTests.csTaskel.Api.TestsE
Taskel.Api.Tests.AuthApiTests$Register_SignsIn_And_Me_Returns_UserF
Taskel.Api.Tests.AuthApiTests%Me_WithoutSession_ReturnsUnauthorizedS
Taskel.Api.Tests.AuthApiTests2Register_DuplicateEmail_IsCaseInsensitive_ConflictH
Taskel.Api.Tests.AuthApiTests'Register_InvalidInput_ReturnsBadRequestG
Taskel.Api.Tests.AuthApiTests&Login_Works_With_Correct_Password_Only4
Taskel.Api.Tests.AuthApiTestsLogout_Ends_Session:
Taskel.Api.Tests.AuthApiTestsPassword_Is_Stored_Hashed¢
A/home/user/taskel/backend/tests/Taskel.Api.Tests/ApiTokenTests.csTaskel.Api.TestsB
Taskel.Api.Tests.ApiTokenTests Token_Authenticates_As_Its_OwnerO
Taskel.Api.Tests.ApiTokenTests-Token_Is_Shown_Once_And_Stored_Only_As_A_Hash;
Taskel.Api.Tests.ApiTokenTestsUnknown_Token_Is_Rejected\
Taskel.Api.Tests.ApiTokenTests:Invalid_Token_Is_Rejected_Even_With_A_Valid_Session_Cookie=
Taskel.Api.Tests.ApiTokenTestsRevoked_Token_Stops_Working<
Taskel.Api.Tests.ApiTokenTestsToken_Cannot_Manage_TokensO
Taskel.Api.Tests.ApiTokenTests-Users_Cannot_See_Or_Revoke_Each_Others_TokensE
Taskel.Api.Tests.ApiTokenTests#Token_Management_Requires_A_Session7
Taskel.Api.Tests.ApiTokenTestsLast_Used_Is_RecordedM
Taskel.Api.Tests.ApiTokenTests+Token_Count_Is_Limited_And_Name_Is_Required