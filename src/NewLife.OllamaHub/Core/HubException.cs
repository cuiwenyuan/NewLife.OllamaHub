using System;
using System.Net;

namespace NewLife.OllamaHub.Core;

/// <summary>
/// 带 HTTP 状态码的业务异常。
/// 真实 Ollama 对不同错误返回不同状态码（未知模型 404、参数非法 400、上游故障 502），
/// Copilot 据此决定是否重试或降级；统一返回 500 会导致客户端行为异常。
/// </summary>
public class HubException : Exception
{
    /// <summary>应返回给客户端的 HTTP 状态码。</summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>构造带状态码的异常。</summary>
    /// <param name="statusCode">HTTP 状态码。</param>
    /// <param name="message">错误描述（将原样写入 {"error":"..."}）。</param>
    /// <param name="innerException">内部异常，可为 null。</param>
    public HubException(HttpStatusCode statusCode, String message, Exception? innerException = null)
        : base(message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>请求参数非法（400）。</summary>
    /// <param name="message">错误描述。</param>
    /// <returns>异常实例。</returns>
    public static HubException BadRequest(String message) => new(HttpStatusCode.BadRequest, message);

    /// <summary>资源不存在，如未注册的模型（404）。</summary>
    /// <param name="message">错误描述。</param>
    /// <returns>异常实例。</returns>
    public static HubException NotFound(String message) => new(HttpStatusCode.NotFound, message);

    /// <summary>上游供应商故障或返回错误（502）。</summary>
    /// <param name="message">错误描述。</param>
    /// <param name="innerException">内部异常，可为 null。</param>
    /// <returns>异常实例。</returns>
    public static HubException BadGateway(String message, Exception? innerException = null)
        => new(HttpStatusCode.BadGateway, message, innerException);
}
