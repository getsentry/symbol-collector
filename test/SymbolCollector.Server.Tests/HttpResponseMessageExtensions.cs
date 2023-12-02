using System.Net;

namespace SymbolCollector.Server.Tests;

internal static class HttpResponseMessageExtensions
{
    public static void AssertStatusCode(this HttpResponseMessage response, HttpStatusCode expectedStatusCode)
    {
        try
        {
            Xunit.Assert.Equal(expectedStatusCode, response.StatusCode);
        }
        catch (Exception e)
        {
            throw new Exception("Response: " + response.Content.ReadAsStringAsync().GetAwaiter().GetResult(), e);
        }
    }
}