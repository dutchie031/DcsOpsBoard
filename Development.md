
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

