using DispatchService.Models;
using MySqlConnector;

namespace DispatchService.Repositories;

public class TechnicianRepository : ITechnicianRepository
{
    private readonly IDbConnectionFactory _connectionFactory;

    public TechnicianRepository(IDbConnectionFactory connectionFactory)
    {
        _connectionFactory = connectionFactory;
    }

    public async Task<bool> ReferenceExistsAsync(string reference)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT 1 FROM technicians WHERE technician_reference = @reference LIMIT 1;";
        command.Parameters.AddWithValue("@reference", reference);
        return await command.ExecuteScalarAsync() is not null;
    }

    public async Task CreateAsync(Technician technician)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = @"
INSERT INTO technicians (id, technician_reference, full_name, region, status, phone, email)
VALUES (@id, @reference, @fullName, @region, @status, @phone, @email);";
                command.Parameters.AddWithValue("@id", technician.Id);
                command.Parameters.AddWithValue("@reference", technician.Reference);
                command.Parameters.AddWithValue("@fullName", technician.FullName);
                command.Parameters.AddWithValue("@region", technician.Region);
                command.Parameters.AddWithValue("@status", technician.Status);
                command.Parameters.AddWithValue("@phone", (object?)technician.Phone ?? DBNull.Value);
                command.Parameters.AddWithValue("@email", (object?)technician.Email ?? DBNull.Value);
                await command.ExecuteNonQueryAsync();
            }

            foreach (var skill in technician.Skills)
            {
                await using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "INSERT INTO technician_skills (technician_id, skill) VALUES (@id, @skill);";
                command.Parameters.AddWithValue("@id", technician.Id);
                command.Parameters.AddWithValue("@skill", skill);
                await command.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<Technician?> GetByIdAsync(string id)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT t.id, t.technician_reference, t.full_name, t.region, t.status, t.phone, t.email, t.created_at, t.updated_at, ts.skill
FROM technicians t
LEFT JOIN technician_skills ts ON ts.technician_id = t.id
WHERE t.id = @id
ORDER BY ts.skill;";
        command.Parameters.AddWithValue("@id", id);

        await using var reader = await command.ExecuteReaderAsync();
        Technician? technician = null;
        var skills = new List<string>();
        var idOrdinal = reader.GetOrdinal("id");
        var referenceOrdinal = reader.GetOrdinal("technician_reference");
        var fullNameOrdinal = reader.GetOrdinal("full_name");
        var regionOrdinal = reader.GetOrdinal("region");
        var statusOrdinal = reader.GetOrdinal("status");
        var phoneOrdinal = reader.GetOrdinal("phone");
        var emailOrdinal = reader.GetOrdinal("email");
        var createdAtOrdinal = reader.GetOrdinal("created_at");
        var updatedAtOrdinal = reader.GetOrdinal("updated_at");
        var skillOrdinal = reader.GetOrdinal("skill");
        while (await reader.ReadAsync())
        {
            technician ??= new Technician
            {
                Id = reader.GetString(idOrdinal),
                Reference = reader.GetString(referenceOrdinal),
                FullName = reader.GetString(fullNameOrdinal),
                Region = reader.GetString(regionOrdinal),
                Status = reader.GetString(statusOrdinal),
                Phone = reader.IsDBNull(phoneOrdinal) ? null : reader.GetString(phoneOrdinal),
                Email = reader.IsDBNull(emailOrdinal) ? null : reader.GetString(emailOrdinal),
                CreatedAt = reader.GetDateTime(createdAtOrdinal),
                UpdatedAt = reader.GetDateTime(updatedAtOrdinal),
            };
            if (!reader.IsDBNull(skillOrdinal)) skills.Add(reader.GetString(skillOrdinal));
        }

        if (technician is not null) technician.Skills = skills;
        return technician;
    }

    public async Task<bool> UpdateAsync(Technician technician)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await using (var update = connection.CreateCommand())
            {
                update.Transaction = transaction;
                update.CommandText = @"
UPDATE technicians
SET full_name = @fullName, region = @region, status = @status, phone = @phone, email = @email
WHERE id = @id;";
                update.Parameters.AddWithValue("@id", technician.Id);
                update.Parameters.AddWithValue("@fullName", technician.FullName);
                update.Parameters.AddWithValue("@region", technician.Region);
                update.Parameters.AddWithValue("@status", technician.Status);
                update.Parameters.AddWithValue("@phone", (object?)technician.Phone ?? DBNull.Value);
                update.Parameters.AddWithValue("@email", (object?)technician.Email ?? DBNull.Value);
                if (await update.ExecuteNonQueryAsync() == 0)
                {
                    await transaction.RollbackAsync();
                    return false;
                }
            }

            await using (var deleteSkills = connection.CreateCommand())
            {
                deleteSkills.Transaction = transaction;
                deleteSkills.CommandText = "DELETE FROM technician_skills WHERE technician_id = @id;";
                deleteSkills.Parameters.AddWithValue("@id", technician.Id);
                await deleteSkills.ExecuteNonQueryAsync();
            }

            foreach (var skill in technician.Skills)
            {
                await using var insertSkill = connection.CreateCommand();
                insertSkill.Transaction = transaction;
                insertSkill.CommandText = "INSERT INTO technician_skills (technician_id, skill) VALUES (@id, @skill);";
                insertSkill.Parameters.AddWithValue("@id", technician.Id);
                insertSkill.Parameters.AddWithValue("@skill", skill);
                await insertSkill.ExecuteNonQueryAsync();
            }

            await transaction.CommitAsync();
            return true;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<TechnicianDeactivationPersistenceResult> DeactivateAsync(string id)
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();

        try
        {
            await using var statusCommand = connection.CreateCommand();
            statusCommand.Transaction = transaction;
            statusCommand.CommandText = "SELECT status FROM technicians WHERE id = @id FOR UPDATE;";
            statusCommand.Parameters.AddWithValue("@id", id);
            var status = await statusCommand.ExecuteScalarAsync() as string;

            if (status is null)
            {
                await transaction.RollbackAsync();
                return TechnicianDeactivationPersistenceResult.NotFound;
            }

            if (string.Equals(status, "INACTIVE", StringComparison.Ordinal))
            {
                await transaction.CommitAsync();
                return TechnicianDeactivationPersistenceResult.AlreadyInactive;
            }

            await using var openAssignmentCommand = connection.CreateCommand();
            openAssignmentCommand.Transaction = transaction;
            openAssignmentCommand.CommandText = @"
SELECT EXISTS(
    SELECT 1
    FROM technician_assignments
    WHERE technician_id = @id
      AND released_at IS NULL
      AND job_status NOT IN ('COMPLETED', 'CANCELLED'))";
            openAssignmentCommand.Parameters.AddWithValue("@id", id);
            var hasOpenAssignments = Convert.ToInt64(await openAssignmentCommand.ExecuteScalarAsync()) == 1;

            if (hasOpenAssignments)
            {
                await transaction.RollbackAsync();
                return TechnicianDeactivationPersistenceResult.HasOpenAssignments;
            }

            await using var deactivateCommand = connection.CreateCommand();
            deactivateCommand.Transaction = transaction;
            deactivateCommand.CommandText = "UPDATE technicians SET status = 'INACTIVE' WHERE id = @id;";
            deactivateCommand.Parameters.AddWithValue("@id", id);
            await deactivateCommand.ExecuteNonQueryAsync();
            await transaction.CommitAsync();
            return TechnicianDeactivationPersistenceResult.Deactivated;
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }

    public async Task<IReadOnlyList<Technician>> GetAllAsync()
    {
        await using var connection = _connectionFactory.CreateConnection();
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = @"
SELECT t.id, t.technician_reference, t.full_name, t.region, t.status, t.phone, t.email, t.created_at, t.updated_at, ts.skill
FROM technicians t
LEFT JOIN technician_skills ts ON ts.technician_id = t.id
ORDER BY t.full_name, t.technician_reference, ts.skill;";

        await using var reader = await command.ExecuteReaderAsync();
        var technicians = new Dictionary<string, Technician>(StringComparer.Ordinal);
        var idOrdinal = reader.GetOrdinal("id");
        var referenceOrdinal = reader.GetOrdinal("technician_reference");
        var fullNameOrdinal = reader.GetOrdinal("full_name");
        var regionOrdinal = reader.GetOrdinal("region");
        var statusOrdinal = reader.GetOrdinal("status");
        var phoneOrdinal = reader.GetOrdinal("phone");
        var emailOrdinal = reader.GetOrdinal("email");
        var createdAtOrdinal = reader.GetOrdinal("created_at");
        var updatedAtOrdinal = reader.GetOrdinal("updated_at");
        var skillOrdinal = reader.GetOrdinal("skill");

        while (await reader.ReadAsync())
        {
            var id = reader.GetString(idOrdinal);
            if (!technicians.TryGetValue(id, out var technician))
            {
                technician = new Technician
                {
                    Id = id,
                    Reference = reader.GetString(referenceOrdinal),
                    FullName = reader.GetString(fullNameOrdinal),
                    Region = reader.GetString(regionOrdinal),
                    Status = reader.GetString(statusOrdinal),
                    Phone = reader.IsDBNull(phoneOrdinal) ? null : reader.GetString(phoneOrdinal),
                    Email = reader.IsDBNull(emailOrdinal) ? null : reader.GetString(emailOrdinal),
                    CreatedAt = reader.GetDateTime(createdAtOrdinal),
                    UpdatedAt = reader.GetDateTime(updatedAtOrdinal),
                };
                technicians.Add(id, technician);
            }

            if (!reader.IsDBNull(skillOrdinal))
                technician.Skills = technician.Skills.Append(reader.GetString(skillOrdinal)).ToList();
        }

        return technicians.Values.ToList();
    }
}
