using KPI.Domain.Entities;
using KPI.Domain.Interfaces;
using KPI.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace KPI.Infrastructure.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    // The repository lives for one request. Authorization and the service both ask for the same user
    // with permissions, so remember the answer instead of paying the database round trip twice.
    private readonly Dictionary<Guid, User?> _withPermissions = new();

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByEmailAsync(string email)
        => await _context.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email.ToLower());

    public async Task<User?> GetByIdAsync(Guid id)
    => await _context.Users
        .Include(u => u.Role)
        .Include(u => u.Department)
        .FirstOrDefaultAsync(u => u.Id == id);

    public async Task<User> CreateAsync(User user)
    {
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    public async Task UpdateAsync(User user)
    {
        _context.Users.Update(user);
        await _context.SaveChangesAsync();
    }

    public async Task<bool> EmailExistsAsync(string email)
        => await _context.Users.AnyAsync(u => u.Email.ToLower() == email.ToLower());

    public async Task<User?> GetByIdWithPermissionsAsync(Guid id)
    {
        if (_withPermissions.TryGetValue(id, out var cached)) return cached;

        var user = await _context.Users
            .Include(u => u.Department)
            .Include(u => u.Role)
                .ThenInclude(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
            .FirstOrDefaultAsync(u => u.Id == id);

        _withPermissions[id] = user;
        return user;
    }
}
