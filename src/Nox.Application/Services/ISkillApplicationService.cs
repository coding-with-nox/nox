using Nox.Application.Commands;
using Nox.Domain.Skills;

namespace Nox.Application.Services;

public interface ISkillApplicationService
{
    Task<Skill> ApproveAsync(ApproveSkillCommand command, CancellationToken ct = default);
    Task<Skill> RejectAsync(RejectSkillCommand command, CancellationToken ct = default);
}
