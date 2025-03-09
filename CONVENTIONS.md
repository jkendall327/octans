Use structured logging, i.e. logger.LogInformation("Foo: {Value}", value);
Avoid pointless logs - only add what's meaningful.
Use .BeginScope with a Dictionary<string, object?> when logging if it cuts down on repetition.
Prefer 'var'.
Avoid excessive indentation; prefer early-returns and guard clauses.
Avoid pointless comments that simply explain what the code is doing.
Place comments on the line above what they describe, not in-line.
Use System.Thread.Lock for creating locks: this is a new .NET feature.
Use primary constructors.
When writing tests, avoid pointless // Arrange // Act comments unless the test is actually complex.