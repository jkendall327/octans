## Code style
Prefer 'var' over specifying the type explicitly.

Avoid excessive indentation; prefer early-returns and guard clauses.

Use System.Thread.Lock for creating locks: this is a new .NET feature. They look like this:

```csharp
public class Foo(ILogger<Foo> logger)
{
    private readonly Lock _lock = new();

    public void Example()
    {
        lock (_lock)
        {
            // ...
        }
    }
}

```

Do NOT convert uses of System.Thread.Lock back to locking on a standard `object`.

Use primary constructors. Primary constructors are a new feature in .NET. They look like this:

```csharp
public class Foo(ILogger<Foo> logger)
{
    public void Example() => logger.LogInformation("Test");
}
```

Do NOT convert primary constructors back to traditional constructors.

## Tests
When writing tests, avoid pointless '// Arrange' or '// Act' comments unless the test is actually complex.

Do not use reflection when writing tests. 
If a test would require reflection, either don't implement it or change the SUT so reflection is not required. 

## Comments
Avoid pointless comments that simply explain what the code is doing.

Place comments on the line above what they describe, not in-line.

## Logging

Use structured logging, i.e. `logger.LogInformation("Foo: {Value}", value);`

Avoid pointless logs - only add what's meaningful.

Use `.BeginScope()` with a `Dictionary<string, object?>` when logging if it cuts down on repetition.