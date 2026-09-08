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
}
