using FluentAssertions;
using Moq;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Application.Performance.DTOs;
using PeopleCore.Application.Performance.Interfaces;
using PeopleCore.Application.Performance.Services;
using PeopleCore.Domain.Entities.Performance;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;
using Xunit;

namespace PeopleCore.Application.Tests.Performance;

public class PerformanceReviewServiceTests
{
    private readonly Mock<IPerformanceReviewRepository> _reviewRepo = new();
    private readonly Mock<IEmployeeRepository> _employeeRepo = new();
    private readonly PerformanceReviewService _sut;

    public PerformanceReviewServiceTests()
    {
        _sut = new PerformanceReviewService(_reviewRepo.Object, _employeeRepo.Object);
    }

    [Fact]
    public async Task SubmitSelfEvaluationAsync_WhenNotTheEmployee_ThrowsDomainException()
    {
        var review = new PerformanceReview
        {
            Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(),
            ReviewCycleId = Guid.NewGuid(), ReviewerId = Guid.NewGuid(),
            Status = ReviewStatus.Draft
        };
        _reviewRepo.Setup(r => r.GetByIdAsync(review.Id, It.IsAny<CancellationToken>())).ReturnsAsync(review);

        var act = () => _sut.SubmitSelfEvaluationAsync(review.Id, Guid.NewGuid(),
            new SubmitSelfEvaluationDto(4.5m, "Good", []));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*only submit your own*");
    }

    [Fact]
    public async Task SubmitManagerReviewAsync_WhenNotTheReviewer_ThrowsDomainException()
    {
        var review = new PerformanceReview
        {
            Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(),
            ReviewCycleId = Guid.NewGuid(), ReviewerId = Guid.NewGuid(),
            Status = ReviewStatus.Submitted, SelfEvaluationScore = 4.0m
        };
        _reviewRepo.Setup(r => r.GetByIdAsync(review.Id, It.IsAny<CancellationToken>())).ReturnsAsync(review);

        var act = () => _sut.SubmitManagerReviewAsync(review.Id, Guid.NewGuid(),
            new SubmitManagerReviewDto(4.2m, "Good", []));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*assigned reviewer*");
    }

    [Fact]
    public async Task SubmitManagerReviewAsync_WhenSelfEvaluationNotSubmitted_ThrowsDomainException()
    {
        var reviewerId = Guid.NewGuid();
        var review = new PerformanceReview
        {
            Id = Guid.NewGuid(), EmployeeId = Guid.NewGuid(),
            ReviewCycleId = Guid.NewGuid(), ReviewerId = reviewerId,
            Status = ReviewStatus.Draft
        };
        _reviewRepo.Setup(r => r.GetByIdAsync(review.Id, It.IsAny<CancellationToken>())).ReturnsAsync(review);

        var act = () => _sut.SubmitManagerReviewAsync(review.Id, reviewerId,
            new SubmitManagerReviewDto(4.2m, "Good", []));

        await act.Should().ThrowAsync<DomainException>().WithMessage("*self-evaluation*");
    }
}
