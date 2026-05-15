
# Welcome

Hi and welcome as a co-contributor or interested dev. This is a guide that is aimed at developers.

## Getting started

### Secrets

The project uses secrets in order to get the OAuth client id and secret going. 


You'll need to set the following secrets: 

```bash
# You'll need to be in the DcsOpsBoard Project directory to run this command
cd DcsOpsBoard 

# Then run the following command to set the secrets
dotnet user-secrets set "DiscordAuthentication:ClientId" "your-client-id"
dotnet user-secrets set "DiscordAuthentication:ClientSecret" "your-client-secret"
```

### Database Migrations

The project uses Entity Framework Core for database access, and migrations are used to manage changes to the database schema. To add a new migration, you can use the following command:

```bash
dotnet ef migrations add <MigrationName> --project DcsOpsBoard.Database --startup-project DcsOpsBoard
```

To remove the last migration, you can use the following command:

```bash
dotnet ef migrations remove --project DcsOpsBoard.Database --startup-project DcsOpsBoard
```