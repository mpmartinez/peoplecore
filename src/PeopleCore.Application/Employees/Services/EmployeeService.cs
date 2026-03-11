using PeopleCore.Application.Common.DTOs;
using PeopleCore.Application.Employees.DTOs;
using PeopleCore.Application.Employees.Interfaces;
using PeopleCore.Domain.Entities.Employees;
using PeopleCore.Domain.Exceptions;

namespace PeopleCore.Application.Employees.Services;

public class EmployeeService : IEmployeeService
{
    private readonly IEmployeeRepository _repo;

    public EmployeeService(IEmployeeRepository repo) => _repo = repo;

    public async Task<PagedResult<EmployeeDto>> GetAllAsync(EmployeeFilterDto filter, CancellationToken ct = default)
    {
        var (items, total) = await _repo.GetPagedAsync(filter, ct);
        return PagedResult<EmployeeDto>.Create(items.Select(ToDto).ToList(), total, filter.Page, filter.PageSize);
    }

    public async Task<EmployeeDto> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Employee {id} not found.");
        return ToDto(employee);
    }

    public async Task<EmployeeDto> CreateAsync(CreateEmployeeDto dto, CancellationToken ct = default)
    {
        if (await _repo.EmployeeNumberExistsAsync(dto.EmployeeNumber, ct))
            throw new DomainException($"Employee number '{dto.EmployeeNumber}' already exists.");

        var employee = new Employee
        {
            EmployeeNumber = dto.EmployeeNumber,
            FirstName = dto.FirstName,
            MiddleName = dto.MiddleName,
            LastName = dto.LastName,
            DateOfBirth = dto.DateOfBirth,
            Gender = dto.Gender,
            WorkEmail = dto.WorkEmail,
            MobileNumber = dto.MobileNumber,
            DepartmentId = dto.DepartmentId,
            PositionId = dto.PositionId,
            ReportingManagerId = dto.ReportingManagerId,
            EmploymentStatus = dto.EmploymentStatus,
            EmploymentType = dto.EmploymentType,
            HireDate = dto.HireDate,
            IsActive = true,
            Is13thMonthEligible = true
        };

        var created = await _repo.AddAsync(employee, ct);
        return ToDto(created);
    }

    public async Task<EmployeeDto> UpdateAsync(Guid id, UpdateEmployeeDto dto, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Employee {id} not found.");

        employee.FirstName = dto.FirstName;
        employee.MiddleName = dto.MiddleName;
        employee.LastName = dto.LastName;
        employee.CivilStatus = dto.CivilStatus;
        employee.PersonalEmail = dto.PersonalEmail;
        employee.MobileNumber = dto.MobileNumber;
        employee.Address = dto.Address;
        employee.DepartmentId = dto.DepartmentId;
        employee.PositionId = dto.PositionId;
        employee.TeamId = dto.TeamId;
        employee.ReportingManagerId = dto.ReportingManagerId;
        employee.EmploymentStatus = dto.EmploymentStatus;
        employee.RegularizationDate = dto.RegularizationDate;
        employee.Is13thMonthEligible = dto.Is13thMonthEligible;
        employee.UpdatedAt = DateTime.UtcNow;

        await _repo.UpdateAsync(employee, ct);
        return ToDto(employee);
    }

    public async Task DeactivateAsync(Guid id, DateOnly separationDate, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(id, ct)
            ?? throw new KeyNotFoundException($"Employee {id} not found.");
        if (!employee.IsActive)
            throw new DomainException("Employee is already inactive.");

        employee.IsActive = false;
        employee.SeparationDate = separationDate;
        employee.UpdatedAt = DateTime.UtcNow;
        await _repo.UpdateAsync(employee, ct);
    }

    public async Task<IReadOnlyList<GovernmentIdDto>> GetGovernmentIdsAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        return employee.GovernmentIds.Select(g => new GovernmentIdDto(g.Id, g.IdType, g.IdNumber)).ToList();
    }

    public async Task UpsertGovernmentIdAsync(Guid employeeId, UpsertGovernmentIdDto dto, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");

        var existing = employee.GovernmentIds.FirstOrDefault(g => g.IdType == dto.IdType);
        if (existing is not null)
        {
            existing.IdNumber = dto.IdNumber;
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            employee.GovernmentIds.Add(new EmployeeGovernmentId
            {
                EmployeeId = employeeId,
                IdType = dto.IdType,
                IdNumber = dto.IdNumber
            });
        }
        await _repo.UpdateAsync(employee, ct);
    }

    public async Task<IReadOnlyList<EmergencyContactDto>> GetEmergencyContactsAsync(Guid employeeId, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        return employee.EmergencyContacts
            .Select(c => new EmergencyContactDto(c.Id, c.Name, c.Relationship, c.Phone, c.Address))
            .ToList();
    }

    public async Task<EmergencyContactDto> AddEmergencyContactAsync(Guid employeeId, CreateEmergencyContactDto dto, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        var contact = new EmergencyContact
        {
            EmployeeId = employeeId,
            Name = dto.Name,
            Relationship = dto.Relationship,
            Phone = dto.Phone,
            Address = dto.Address
        };
        employee.EmergencyContacts.Add(contact);
        await _repo.UpdateAsync(employee, ct);
        return new EmergencyContactDto(contact.Id, contact.Name, contact.Relationship, contact.Phone, contact.Address);
    }

    public async Task DeleteEmergencyContactAsync(Guid employeeId, Guid contactId, CancellationToken ct = default)
    {
        var employee = await _repo.GetByIdAsync(employeeId, ct)
            ?? throw new KeyNotFoundException($"Employee {employeeId} not found.");
        var contact = employee.EmergencyContacts.FirstOrDefault(c => c.Id == contactId)
            ?? throw new KeyNotFoundException($"Emergency contact {contactId} not found.");
        employee.EmergencyContacts.Remove(contact);
        await _repo.UpdateAsync(employee, ct);
    }

    private static EmployeeDto ToDto(Employee e) => new(
        e.Id, e.EmployeeNumber, e.FirstName, e.MiddleName, e.LastName, e.FullName,
        e.DateOfBirth, e.Gender, e.CivilStatus, e.WorkEmail, e.MobileNumber,
        e.DepartmentId, e.Department?.Name,
        e.PositionId, e.Position?.Title,
        e.ReportingManagerId, e.ReportingManager?.FullName,
        e.TeamId,
        e.EmploymentStatus, e.EmploymentType, e.HireDate, e.RegularizationDate,
        e.IsActive, e.Is13thMonthEligible);
}
