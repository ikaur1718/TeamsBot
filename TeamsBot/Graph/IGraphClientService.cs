using Microsoft.Graph;

namespace TeamsBot.Graph
{
    public interface IGraphClientService
    {
        GraphServiceClient GetAuthenticatedGraphClient(string accessToken);
    }
}
