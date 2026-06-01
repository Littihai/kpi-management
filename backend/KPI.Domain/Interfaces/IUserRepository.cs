using KPI.Domain.Entities;

namespace KPI.Domain.Interfaces;

public interface IUserRepository
{
    Task<User?> GetByEmailAsync(string email);
    Task<User?> GetByIdAsync(Guid id);
    Task<User> CreateAsync(User user);
    Task<bool> EmailExistsAsync(string email);
}