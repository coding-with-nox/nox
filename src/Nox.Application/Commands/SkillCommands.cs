namespace Nox.Application.Commands;

/// <summary>approvedBy must come from the authenticated identity.</summary>
public record ApproveSkillCommand(Guid SkillId, string ApprovedBy);

/// <summary>rejectedBy must come from the authenticated identity.</summary>
public record RejectSkillCommand(Guid SkillId, string RejectedBy, string Reason);
