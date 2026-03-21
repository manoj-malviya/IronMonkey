namespace IronMonkey.ApiService.Common;

public interface IEndpoint
{
    static abstract void Map(IEndpointRouteBuilder app);
}