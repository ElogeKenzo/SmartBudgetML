using System.Linq.Expressions;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Generic repository interface providing asynchronous CRUD operations
/// for any SQLite entity implementing <see cref="IEntity"/>.
/// </summary>
/// <typeparam name="T">Entity type</typeparam>
public interface IRepository<T> where T : class, IEntity, new()
{
    Task<T?> GetByIdAsync(int id);
    Task<List<T>> GetAllAsync();
    Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate);
    Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate);
    Task<int> InsertAsync(T entity);
    Task<int> InsertAllAsync(IEnumerable<T> entities);
    Task<int> UpdateAsync(T entity);
    Task<int> DeleteAsync(T entity);
    Task<int> DeleteByIdAsync(int id);
    Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null);
}
