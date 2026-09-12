using PeopleCore.Application.Careers.DTOs;
using PeopleCore.Application.Careers.Interfaces;
using PeopleCore.Application.Common.Interfaces;
using PeopleCore.Application.Common.Options;
using PeopleCore.Application.Recruitment.Interfaces;
using PeopleCore.Domain.Entities.Recruitment;
using PeopleCore.Domain.Enums;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Careers.Services;

public class CareersService : ICareersService
{
    private readonly IJobPostingRepository _jobPostingRepo;
    private readonly IApplicantRepository _applicantRepo;
    private readonly IStorageService _storageService;
    private readonly DocumentStorageOptions _storageOptions;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".pdf", ".docx"
    };

    private const long MaxResumeSizeBytes = 5 * 1024 * 1024; // 5 MB

    public CareersService(
        IJobPostingRepository jobPostingRepo,
        IApplicantRepository applicantRepo,
        IStorageService storageService,
        DocumentStorageOptions storageOptions)
    {
        _jobPostingRepo = jobPostingRepo;
        _applicantRepo = applicantRepo;
        _storageService = storageService;
        _storageOptions = storageOptions;
    }

    public async Task<IReadOnlyList<PublicJobPostingDto>> GetOpenJobsAsync(CancellationToken ct = default)
    {
        var all = await _jobPostingRepo.GetAllAsync(ct);
        return all
            .Where(j => j.Status == JobPostingStatus.Open)
            .Select(MapToDto)
            .ToList();
    }

    public async Task<PublicJobPostingDto?> GetJobAsync(Guid id, CancellationToken ct = default)
    {
        var job = await _jobPostingRepo.GetByIdAsync(id, ct);
        if (job is null || job.Status != JobPostingStatus.Open)
            return null;

        return MapToDto(job);
    }

    public async Task<JobApplicationResponse> ApplyAsync(Guid jobPostingId, JobApplicationRequest request, CancellationToken ct = default)
    {
        var jobPosting = await _jobPostingRepo.GetByIdAsync(jobPostingId, ct)
            ?? throw new KeyNotFoundException($"Job posting {jobPostingId} not found.");

        if (jobPosting.Status != JobPostingStatus.Open)
            throw new DomainException($"Job posting {jobPostingId} is not open for applications.");

        // Check for duplicate email on same job
        var existingApplicants = await _applicantRepo.GetAllAsync(ct);
        if (existingApplicants.Any(a => a.JobPostingId == jobPostingId &&
                                        a.Email.Equals(request.Email, StringComparison.OrdinalIgnoreCase)))
        {
            throw new DomainException($"An application with email '{request.Email}' already exists for this job posting.");
        }

        // Validate resume
        var extension = Path.GetExtension(request.ResumeFileName);
        if (!AllowedExtensions.Contains(extension))
            throw new DomainException($"Resume file type '{extension}' is not supported. Allowed: .pdf, .docx");

        var resumeBytes = Convert.FromBase64String(request.ResumeBase64);
        if (resumeBytes.Length > MaxResumeSizeBytes)
            throw new DomainException("Resume file size exceeds the 5 MB limit.");

        // Upload resume
        var objectKey = $"resumes/{Guid.NewGuid()}{extension}";
        var contentType = extension.ToLowerInvariant() switch
        {
            ".pdf" => "application/pdf",
            ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            _ => "application/octet-stream"
        };

        string storageKey;
        using (var stream = new MemoryStream(resumeBytes))
        {
            storageKey = await _storageService.UploadAsync(_storageOptions.ResumesBucketName, objectKey, stream, contentType, ct);
        }

        // Create applicant
        var applicant = new Applicant
        {
            JobPostingId = jobPostingId,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
            Phone = request.Phone,
            ResumeStorageKey = storageKey,
            Status = ApplicantStatus.Applied,
            AppliedAt = DateTime.UtcNow
        };

        var saved = await _applicantRepo.AddAsync(applicant, ct);

        return new JobApplicationResponse(saved.Id, "Application submitted successfully.");
    }

    private static PublicJobPostingDto MapToDto(JobPosting j) => new(
        Id: j.Id,
        Title: j.Title,
        Description: j.Description ?? string.Empty,
        Requirements: j.Requirements ?? string.Empty,
        DepartmentName: j.Department?.Name,
        PositionTitle: j.Position?.Title,
        Vacancies: j.Vacancies,
        PostedAt: j.PostedAt ?? DateTime.MinValue);
}
