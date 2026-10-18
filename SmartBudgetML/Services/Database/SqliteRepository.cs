using System.Linq.Expressions;
using SQLite;
using SmartBudgetML.Models;

namespace SmartBudgetML.Services.Database;

/// <summary>
/// Concrete SQLite generic repository providing async CRUD operations.
/// </summary>
/// <typeparam name="T">Entity type implementing <see cref="IEntity"/></typeparam>
public class SqliteRepository<T> : IRepository<T> where T : class, IEntity, new()
{
    private readonly SQLiteAsyncConnection _connection;

    public SqliteRepository(SQLiteAsyncConnection connection)
    {
        _connection = connection ?? throw new ArgumentNullException(nameof(connection));
    }

    public async Task<T?> GetByIdAsync(int id)
    {
        return await _connection.Table<T>().Where(e => e.Id == id).FirstOrDefaultAsync();
    }

    public async Task<List<T>> GetAllAsync()
    {
        return await _connection.Table<T>().ToListAsync();
    }

    public async Task<List<T>> FindAsync(Expression<Func<T, bool>> predicate)
    {
        return await _connection.Table<T>().Where(predicate).ToListAsync();
    }

    public async Task<T?> FirstOrDefaultAsync(Expression<Func<T, bool>> predicate)
    {
        return await _connection.Table<T>().Where(predicate).FirstOrDefaultAsync();
    }

    public async Task<int> InsertAsync(T entity)
    {
        return await _connection.InsertAsync(entity);
    }

    public async Task<int> InsertAllAsync(IEnumerable<T> entities)
    {
        return await _connection.InsertAllAsync(entities);
    }

    public async Task<int> UpdateAsync(T entity)
    {
        return await _connection.UpdateAsync(entity);
    }

    public async Task<int> DeleteAsync(T entity)
    {
        return await _connection.DeleteAsync(entity);
    }

    public async Task<int> DeleteByIdAsync(int id)
    {
        return await _connection.DeleteAsync<T>(id);
    }

    public async Task<int> CountAsync(Expression<Func<T, bool>>? predicate = null)
    {
        if (predicate == null)
        {
            return await _connection.Table<T>().CountAsync();
        }

        return await _connection.Table<T>().Where(predicate).CountAsync();
    }
}
