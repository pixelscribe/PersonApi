using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonApi.Data;
using PersonApi.Models;

namespace PersonApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PersonController(AppDbContext db) : ControllerBase
{
    // GET /api/person
    [HttpGet]
    public async Task<ActionResult<IEnumerable<Person>>> GetAll()
    {
        return await db.People.OrderBy(p => p.Id).ToListAsync();
    }

    // GET /api/person/search?q=jane+doe
    // Uses the FULLTEXT index on (first_name, last_name) rather than LIKE '%...%'.
    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<Person>>> Search([FromQuery] string q)
    {
        if (string.IsNullOrWhiteSpace(q))
        {
            return BadRequest("Query parameter 'q' is required.");
        }

        var results = await db.People
            .FromSqlInterpolated($@"
                SELECT * FROM person
                WHERE MATCH(first_name, last_name) AGAINST ({q} IN NATURAL LANGUAGE MODE)")
            .ToListAsync();

        return results;
    }

    // GET /api/person/5
    [HttpGet("{id}")]
    public async Task<ActionResult<Person>> GetById(ulong id)
    {
        var person = await db.People.FindAsync(id);
        return person is null ? NotFound() : person;
    }

    // POST /api/person
    [HttpPost]
    public async Task<ActionResult<Person>> Create(CreatePersonRequest request)
    {
        var person = new Person
        {
            FirstName = request.FirstName,
            LastName = request.LastName,
            Email = request.Email,
        };

        db.People.Add(person);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateEmail(ex))
        {
            return Conflict($"A person with email '{request.Email}' already exists.");
        }

        return CreatedAtAction(nameof(GetById), new { id = person.Id }, person);
    }

    // PUT /api/person/5
    [HttpPut("{id}")]
    public async Task<IActionResult> Update(ulong id, UpdatePersonRequest request)
    {
        var person = await db.People.FindAsync(id);
        if (person is null)
        {
            return NotFound();
        }

        person.FirstName = request.FirstName;
        person.LastName = request.LastName;
        person.Email = request.Email;

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsDuplicateEmail(ex))
        {
            return Conflict($"A person with email '{request.Email}' already exists.");
        }

        return NoContent();
    }

    // MySQL's duplicate-key violation is error number 1062 (ER_DUP_ENTRY),
    // surfaced by MySqlConnector as MySqlException.Number.
    private static bool IsDuplicateEmail(DbUpdateException ex) =>
        ex.InnerException is MySqlConnector.MySqlException { Number: 1062 };

    // DELETE /api/person/5
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(ulong id)
    {
        var person = await db.People.FindAsync(id);
        if (person is null)
        {
            return NotFound();
        }

        db.People.Remove(person);
        await db.SaveChangesAsync();

        return NoContent();
    }
}
