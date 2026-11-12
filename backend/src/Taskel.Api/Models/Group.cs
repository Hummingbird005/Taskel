using System.ComponentModel.DataAnnotations;
using Taskel.Api.Validation;

namespace Taskel.Api.Models;

public enum GroupKind
{
    /// <summary>Created and maintained in Taskel by an administrator or a manager.</summary>
    App = 0,
    /// <summary>Mirrors an Active Directory group; its members are learned as they sign in.</summary>
    Directory = 1
}

/// <summary>A set of users a task can be given to. Any member can then take the task.</summary>
public class Group
{
    public const int MaxNameLength = 100;
    public const int MaxMembers = 500;

    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public GroupKind Kind { get; set; }
    /// <summary>Distinguished name of the Active Directory group. Null for application groups.</summary>
    public string? DirectoryDn { get; set; }
    /// <summary>Who created an application group. A manager manages only the groups they created.</summary>
    public Guid? CreatedById { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public List<GroupMember> Members { get; set; } = [];
}

public class GroupMember
{
    public Guid GroupId { get; set; }
    public Guid UserId { get; set; }
    public User? User { get; set; }
}

/// <summary>One step in a task's journey: who passed it on, to whom, and when.</summary>
public class TaskForward
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TaskId { get; set; }
    public Guid FromUserId { get; set; }
    public User? From { get; set; }
    public Guid? ToUserId { get; set; }
    public User? ToUser { get; set; }
    public Guid? ToGroupId { get; set; }
    public Group? ToGroup { get; set; }
    public string? Note { get; set; }
    public DateTime At { get; set; } = DateTime.UtcNow;
}

/// <summary>A group as shown in pickers and on tasks.</summary>
public record GroupSummary(Guid Id, string Name, GroupKind Kind)
{
    public static GroupSummary From(Group group) => new(group.Id, group.Name, group.Kind);
}

/// <summary>A group in the list of groups a task can be given to.</summary>
public record GroupListItem(Guid Id, string Name, GroupKind Kind, int MemberCount, bool CanManage);

/// <summary>A group with its members, for whoever manages it.</summary>
public record GroupDetail(Guid Id, string Name, GroupKind Kind, bool CanManage, IReadOnlyList<UserSummary> Members);

public record SaveGroupRequest(
    [Required, StringLength(Group.MaxNameLength, MinimumLength = 1), SafeText] string Name,
    [Required, MaxLength(Group.MaxMembers)] List<Guid> MemberIds);

public record ForwardResponse(UserSummary From, UserSummary? ToUser, GroupSummary? ToGroup, string? Note, DateTime At);
