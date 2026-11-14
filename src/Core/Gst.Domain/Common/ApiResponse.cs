using System.Net;

namespace Gst.Domain.Common
{
    // Har API ka common response format
    public class ApiResponse<T>
    {
        public T? Response { get; set; }
        public HttpStatusCode Code { get; set; }
        public string? Description { get; set; }
        public string? Error { get; set; }

        public static ApiResponse<T> Success(T data, string? message = null, HttpStatusCode code = HttpStatusCode.OK) =>
            new() { Response = data, Code = code, Description = message ?? code.ToString() };

        public static ApiResponse<T> Fail(string error, HttpStatusCode code = HttpStatusCode.BadRequest) =>
            new() { Error = error, Code = code, Description = code.ToString() };
    }
}
