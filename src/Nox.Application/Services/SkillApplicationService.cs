using Nox.Application.Commands;
using Nox.Domain.Skills;

namespace Nox.Application.Services;

public class SkillApplicationService(ISkillRegistry skillRegistry) : ISkillApplicationService
{
    public Task<Skill> ApproveAsync(ApproveSkillCommand command, CancellationToken ct = default)
        => skillRegistry.ApproveSkillAsync(command.SkillId, command.ApprovedBy);

    public Task<Skill> RejectAsync(RejectSkillCommand command, CancellationToken ct = default)
        => skillRegistry.RejectSkillAsync(command.SkillId, command.RejectedBy, command.Reason);
}
