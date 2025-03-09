Use structured logging, i.e. logger.LogInformation("Foo: {Value}", value);
Prefer 'var'.
Avoid excessive indentation; prefer early-returns and guard clauses.
Avoid pointless comments that simply explain what the code is doing.
Place comments on the line above what they describe, not in-line.
When writing tests, avoid pointless // Arrange // Act comments unless the test is actually complex enough to warrant them.