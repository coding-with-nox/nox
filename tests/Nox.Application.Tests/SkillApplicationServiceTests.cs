using Nox.Application.Commands;
using Nox.Application.Services;
using Nox.Domain;
using Nox.Domain.Skills;

namespace Nox.Application.Tests;

public class SkillApplicationServiceTests
{
    private readonly ISkillRegistry _registry = Substitute.For<ISkillRegistry>();
    private readonly SkillApplicationService _sut;

    public SkillApplicationServiceTests()
    {
        _sut = new SkillApplicationService(_registry);
    }

    [Fact]
    public async Task Approve_CallsRegistry_WithCorrectArguments()
    {
        var skillId = Guid.NewGuid();
        var approved = new Skill { Id = skillId, Slug = "test", Name = "Test", Status = SkillStatus.Active };
        _registry.ApproveSkillAsync(skillId, "admin@test.com").Returns(approved);

        var result = await _sut.ApproveAsync(new ApproveSkillCommand(skillId, "admin@test.com"));

        Assert.Equal(SkillStatus.Active, result.Status);
        await _registry.Received(1).ApproveSkillAsync(skillId, "admin@test.com");
    }

    [Fact]
    public async Task Reject_CallsRegistry_WithCorrectArguments()
    {
        var skillId = Guid.NewGuid();
        var rejected = new Skill { Id = skillId, Slug = "test", Name = "Test", Status = SkillStatus.Rejected };
        _registry.RejectSkillAsync(skillId, "admin@test.com", "too broad").Returns(rejected);

        var result = await _sut.RejectAsync(new RejectSkillCommand(skillId, "admin@test.com", "too broad"));

        Assert.Equal(SkillStatus.Rejected, result.Status);
        await _registry.Received(1).RejectSkillAsync(skillId, "admin@test.com", "too broad");
    }
}
