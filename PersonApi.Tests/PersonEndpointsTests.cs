using System.Net;
using System.Net.Http.Json;
using PersonApi.Models;

namespace PersonApi.Tests;

[Collection(PersonApiCollection.Name)]
public class PersonEndpointsTests(PersonApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    private static CreatePersonRequest NewPerson(string? email = null) => new()
    {
        FirstName = "Jane",
        LastName = "Doe",
        Email = email ?? $"{Guid.NewGuid()}@example.com",
    };

    [Fact]
    public async Task Create_ReturnsCreatedWithLocationHeader()
    {
        var response = await _client.PostAsJsonAsync("/api/person", NewPerson());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var person = await response.Content.ReadFromJsonAsync<Person>();
        Assert.NotNull(person);
        Assert.True(person!.Id > 0);
    }

    [Fact]
    public async Task Create_ReturnsConflict_WhenEmailAlreadyExists()
    {
        var request = NewPerson();
        await _client.PostAsJsonAsync("/api/person", request);

        var response = await _client.PostAsJsonAsync("/api/person", request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Create_ReturnsBadRequest_WhenEmailInvalid()
    {
        var request = NewPerson(email: "not-an-email");

        var response = await _client.PostAsJsonAsync("/api/person", request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetById_ReturnsPerson_WhenExists()
    {
        var created = await (await _client.PostAsJsonAsync("/api/person", NewPerson())).Content.ReadFromJsonAsync<Person>();

        var response = await _client.GetAsync($"/api/person/{created!.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var person = await response.Content.ReadFromJsonAsync<Person>();
        Assert.Equal(created.Id, person!.Id);
    }

    [Fact]
    public async Task GetById_ReturnsNotFound_WhenPersonDoesNotExist()
    {
        var response = await _client.GetAsync("/api/person/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetAll_IncludesCreatedPerson()
    {
        var created = await (await _client.PostAsJsonAsync("/api/person", NewPerson())).Content.ReadFromJsonAsync<Person>();

        var people = await _client.GetFromJsonAsync<List<Person>>("/api/person");

        Assert.Contains(people!, p => p.Id == created!.Id);
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        var created = await (await _client.PostAsJsonAsync("/api/person", NewPerson())).Content.ReadFromJsonAsync<Person>();
        var update = new UpdatePersonRequest
        {
            FirstName = "Janet",
            LastName = created!.LastName,
            Email = created.Email,
        };

        var response = await _client.PutAsJsonAsync($"/api/person/{created.Id}", update);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var updated = await _client.GetFromJsonAsync<Person>($"/api/person/{created.Id}");
        Assert.Equal("Janet", updated!.FirstName);
    }

    [Fact]
    public async Task Update_ReturnsNotFound_WhenPersonDoesNotExist()
    {
        var response = await _client.PutAsJsonAsync("/api/person/999999999", NewPerson());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Update_ReturnsConflict_WhenEmailBelongsToSomeoneElse()
    {
        var first = await (await _client.PostAsJsonAsync("/api/person", NewPerson())).Content.ReadFromJsonAsync<Person>();
        var second = await (await _client.PostAsJsonAsync("/api/person", NewPerson())).Content.ReadFromJsonAsync<Person>();
        var update = new UpdatePersonRequest { FirstName = "X", LastName = "Y", Email = first!.Email };

        var response = await _client.PutAsJsonAsync($"/api/person/{second!.Id}", update);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Delete_RemovesPerson()
    {
        var created = await (await _client.PostAsJsonAsync("/api/person", NewPerson())).Content.ReadFromJsonAsync<Person>();

        var response = await _client.DeleteAsync($"/api/person/{created!.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var getResponse = await _client.GetAsync($"/api/person/{created.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }

    [Fact]
    public async Task Delete_ReturnsNotFound_WhenPersonDoesNotExist()
    {
        var response = await _client.DeleteAsync("/api/person/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Search_ReturnsMatchingPeople()
    {
        var uniqueName = $"Zzyzx{Guid.NewGuid():N}";
        await _client.PostAsJsonAsync("/api/person", new CreatePersonRequest
        {
            FirstName = uniqueName,
            LastName = "Searchable",
            Email = $"{Guid.NewGuid()}@example.com",
        });

        var results = await _client.GetFromJsonAsync<List<Person>>($"/api/person/search?q={uniqueName}");

        Assert.Contains(results!, p => p.FirstName == uniqueName);
    }

    [Fact]
    public async Task Search_ReturnsBadRequest_WhenQueryMissing()
    {
        var response = await _client.GetAsync("/api/person/search?q=");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
