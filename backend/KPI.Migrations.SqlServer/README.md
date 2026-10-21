# SQL Server migrations

The same model as the PostgreSQL migrations in KPI.Infrastructure, written in SQL Server's dialect.
Used when `Database:Provider` is `SqlServer` (see DatabaseProvider.cs). Add a migration after changing the model:

    cd backend
    $env:Database__Provider = "SqlServer"
    $env:ConnectionStrings__DefaultConnection = "Server=(localdb)\MSSQLLocalDB;Database=helpdesk_design;Trusted_Connection=True;TrustServerCertificate=True"
    $env:Jwt__Key = "any-random-32-characters-or-more-here"
    dotnet ef migrations add <Name> -p KPI.Migrations.SqlServer -s KPI.API -o Migrations

Keep it in step with the PostgreSQL set: a change to the model needs one migration in each (the tests fail otherwise).
