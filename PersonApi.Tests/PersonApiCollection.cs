namespace PersonApi.Tests;

[CollectionDefinition(Name)]
public class PersonApiCollection : ICollectionFixture<PersonApiFactory>
{
    public const string Name = "PersonApi";
}
