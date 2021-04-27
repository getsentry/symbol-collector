using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;
using Sentry;
using SymbolCollector.Server.Models;

namespace SymbolCollector.Server
{
    [Route(Route)]
    public class SymbolsController : Controller
    {
        public const string Route = "/symbol";

        private readonly long _fileSizeLimit;
        private readonly IHub _hub;
        private readonly ISymbolService _symbolService;
        private readonly ISymbolControllerMetrics _metrics;
        private readonly ILogger<SymbolsController> _logger;
        private readonly char[] _invalidChars;

        private static readonly FormOptions DefaultFormOptions = new FormOptions();

        public SymbolsController(
            IHub hub,
            IConfiguration config,
            ISymbolService symbolService,
            ISymbolControllerMetrics metrics,
            ILogger<SymbolsController> logger)
        {
            _hub = hub;
            _symbolService = symbolService;
            _metrics = metrics;
            _logger = logger;
            _fileSizeLimit = config.GetValue<long>("FileSizeLimitBytes");
            // Don't allow file names with paths encoded.
            _invalidChars = Path.GetInvalidFileNameChars().Concat(new[] {'/', '\\'}).ToArray();
        }

        [HttpGet(Route + "/batch/{batchId}")]
        public async Task<SymbolUploadBatch?> Get([FromRoute] Guid batchId, CancellationToken token)
        {
            var batch = await _symbolService.GetBatch(batchId, token);
            return batch;
        }

        [HttpPost(Route + "/batch/{batchId}/start")]
        public async Task<IActionResult> Start(
            [FromRoute] Guid batchId,
            [FromBody] BatchStartRequestModel model,
            CancellationToken token)
        {
            using var _ = _metrics.BeginOpenBatch();
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _hub.ConfigureScope(s =>
            {
                s.SetTag("batchId", batchId.ToString());
                s.SetTag("friendly-name", model.BatchFriendlyName);
            });

            if (await _symbolService.GetBatch(batchId, token) is {})
            {
                return BadRequest($"Batch Id {batchId} was already used.");
            }

            await _symbolService.Start(batchId, model.BatchFriendlyName, model.BatchType, token);

            return Ok();
        }

        [HttpPost(Route + "/batch/{batchId}/close")]
        public async Task<IActionResult> CloseBatch([FromRoute] Guid batchId, [FromBody] BatchEndRequestModel model,
            CancellationToken token)
        {
            using var _ = _metrics.BeginCloseBatch();
            await ValidateBatch(batchId, ModelState, token);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _hub.ConfigureScope(s =>
            {
                s.SetTag("batchId", batchId.ToString());
            });

            await _symbolService.Finish(batchId, model.ClientMetrics, token);
            return NoContent();
        }

        [HttpHead(Route + "/batch/{batchId}/check/{unifiedId}/{hash?}")]
        public async Task<IActionResult> IsSymbolMissing(
            [FromRoute] Guid batchId,
            [FromRoute] string unifiedId,
            [FromRoute] string? hash,
            CancellationToken token)
        {
            using var _ = _metrics.BeginSymbolMissingCheck();
            await ValidateBatch(batchId, ModelState, token);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _hub.ConfigureScope(s =>
            {
                s.SetTag("batchId", batchId.ToString());
            });

            var symbol = await _symbolService.GetSymbol(unifiedId, token);
            if (symbol is null)
            {
                _logger.LogDebug("{batchId} looked for {unifiedId} and {hash} which is a missing symbol.",
                    batchId, unifiedId, hash);
                _metrics.SymbolCheckMissing();
                return Ok();
            }

            if (hash is {} && symbol.Hash is {} && string.CompareOrdinal(hash, symbol.Hash) != 0)
            {
                using (_logger.BeginScope(new Dictionary<string, string>()
                {
                    {"existing-file-hash", symbol.Hash},
                    {"existing-file-name", symbol.Name},
                    {"new-file-hash", hash},
                }))
                {
                    // Return OK so that client uploads the symbol. The upload handing code
                    // will take the file "aside" for troubleshooting
                    _logger.LogDebug(
                        "File with {unifiedId} as part of {batchId} has a conflicting hash with the existing file.",
                        unifiedId, batchId);
                }

                return Ok();
            }

            if (!symbol.BatchIds.TryGetValue(batchId, out var _))
            {
                await _symbolService.Relate(batchId, symbol, token);
            }

            _metrics.SymbolCheckExists();
            return Conflict();
        }

        [HttpHead(Route + "/batch/{batchId}/check/v2/{unifiedId}/{hash?}")]
        public async Task<IActionResult> IsSymbolMissingV2(
            [FromRoute] Guid batchId,
            [FromRoute] string unifiedId,
            [FromRoute] string? hash,
            CancellationToken token)
        {
            var result = await IsSymbolMissing(batchId, unifiedId, hash, token);
            return result is ConflictResult
                // It's an expected scenario so returning something on the 2xx range
                ? new StatusCodeResult(208)
                : result;
        }

        [HttpPost(Route + "/batch/{batchId}/upload/")]
        [DisableFormValueModelBinding]
        public async Task<IActionResult> UploadSymbol(
            [FromRoute] Guid batchId,
            CancellationToken token)
        {
            using var _ = _metrics.BeginUploadSymbol();
            await ValidateBatch(batchId, ModelState, token);

            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            _hub.ConfigureScope(s =>
            {
                s.SetTag("batchId", batchId.ToString());
            });

            if (!MultipartRequestHelper.IsMultipartContentType(Request.ContentType))
            {
                return BadRequest($"ContentType {Request.ContentType} is not supported.");
            }

            var boundary = MultipartRequestHelper.GetBoundary(
                MediaTypeHeaderValue.Parse(Request.ContentType),
                DefaultFormOptions.MultipartBoundaryLengthLimit);
            var reader = new MultipartReader(boundary, HttpContext.Request.Body);

            var section = await reader.ReadNextSectionAsync(token);

            if (section == null)
            {
                return BadRequest("No file received.");
            }

            var sectionsCount = 0;
            var invalidContentDispositions = new List<string>();
            var results = new List<(string FileName, StoreResult Result)>();
            while (section != null)
            {
                sectionsCount++;
                if (ContentDispositionHeaderValue.TryParse(
                        section.ContentDisposition, out var contentDisposition) && MultipartRequestHelper
                        .HasFileContentDisposition(contentDisposition))
                {
                    var (fileName, data, status) =
                        await ProcessStreamedFile(section, contentDisposition, ModelState, _fileSizeLimit);

                    if (!ModelState.IsValid)
                    {
                        status ??= HttpStatusCode.BadRequest;
                        return StatusCode((int)status, ModelState);
                    }

                    _logger.LogInformation(
                        "Persisting file '{fileName}' with {bytes} bytes.", fileName, data.Length);

                    var fileStoreResult = await _symbolService.Store(
                        batchId,
                        fileName,
                        data,
                        token);

                    switch (fileStoreResult)
                    {
                        case StoreResult.Invalid:
                            _metrics.FileInvalid();
                            break;
                        case StoreResult.Created:
                            _metrics.FileStored(data.Length);
                            break;
                        case StoreResult.AlreadyExisted:
                            _metrics.FileKnown();
                            break;
                    }

                    results.Add((fileName, fileStoreResult));


                    await data.DisposeAsync();
                }
                else
                {
                    if (section.ContentDisposition is null)
                    {
                        _logger.LogWarning("ContentDisposition is null");
                    }
                    else
                    {
                        invalidContentDispositions.Add(section.ContentDisposition);
                        _logger.LogWarning("ContentDisposition not supported: {contentDisposition}",
                            section.ContentDisposition);
                    }
                    _logger.LogWarning("ContentDisposition not supported: {contentDisposition}",
                        section.ContentDisposition);
                }

                section = await reader.ReadNextSectionAsync(token);
            }

            var filesCreated = results.Count(r => r.Result == StoreResult.Created);

            var response = new {filesCreated, results};
            return filesCreated switch
            {
                0 when results.Any(p => p.Result == StoreResult.AlreadyExisted)
                => StatusCode(208, response),
                0 => BadRequest(new
                {
                    errorMessage = "Invalid request. No file accepted.",
                    numberOfSectionsReceived = sectionsCount,
                    invalidContentDispositions,
                    response
                }),
                _ => Created(nameof(SymbolsController), response)
            };
        }

        public async Task ValidateBatch(Guid batchId, ModelStateDictionary modelState, CancellationToken token)
        {
            if (batchId == default)
            {
                modelState.AddModelError("Batch", "A BatchId is required.");
            }
            else
            {
                var batch = await _symbolService.GetBatch(batchId, token);
                if (batch is null)
                {
                    modelState.AddModelError("Batch", $"Batch Id {batchId} does not exist.");
                }
                else if (batch.IsClosed)
                {
                    modelState.AddModelError("Batch", $"Batch Id {batchId} is already closed.");
                }
            }
        }

        private async ValueTask<(string fileName, Stream data, HttpStatusCode? code)> ProcessStreamedFile(
            MultipartSection section, ContentDispositionHeaderValue contentDisposition,
            ModelStateDictionary modelState, long sizeLimitBytes)
        {
            Stream outputStream = new MemoryStream();
            string? fileName = null;
            HttpStatusCode? code = null;
            try
            {
                fileName = contentDisposition.FileName.Value;

                if (section.Headers!.TryGetValue("Content-Encoding", out var contentType)
                    && contentType == "gzip")
                {
                    await using var gzipStream = new GZipStream(section.Body, CompressionMode.Decompress);
                    await gzipStream.CopyToAsync(outputStream);
                }
                else
                {
                    await section.Body.CopyToAsync(outputStream);
                }

                outputStream.Position = 0; // TODO: needs rewinding?

                if (outputStream.Length == 0)
                {
                    modelState.AddModelError("File", "The file is empty.");
                }
                else if (outputStream.Length > sizeLimitBytes)
                {
                    var payloadMegabytesSizeLimit = sizeLimitBytes / 1048576;
                    var limitMegabytesSizeLimit = sizeLimitBytes / 1048576;
                    modelState.AddModelError("File",
                        $"The file size {payloadMegabytesSizeLimit:N1} exceeds {limitMegabytesSizeLimit:N1} MB.");
                    _logger.LogWarning("File name {fileName} is too large: {size} MB.", fileName,
                        payloadMegabytesSizeLimit);
                    code = HttpStatusCode.RequestEntityTooLarge;
                }
                else if (fileName.Any(p => _invalidChars.Contains(p)))
                {
                    ModelState.AddModelError("File", "The file name specified contain invalid characters.");
                    _logger.LogWarning("File name {fileName} contain invalid characters.", fileName);
                }
                else
                {
                    return (fileName, outputStream, null);
                }
            }
            catch (Exception ex)
            {
                modelState.AddModelError("File", $"The upload failed. Error: {ex.HResult}");
                _logger.LogError(ex, "Failed to process file {file}", fileName);
            }

            return ("error", outputStream, code);
        }
    }

    // Copy pasta from: https://github.com/aspnet/AspNetCore.Docs/blob/master/aspnetcore/mvc/models/file-uploads/samples/2.x/SampleApp/Utilities/MultipartRequestHelper.cs
    public static class MultipartRequestHelper
    {
        // Content-Type: multipart/form-data; boundary="----WebKitFormBoundarymx2fSWqWSd0OxQqq"
        // The spec at https://tools.ietf.org/html/rfc2046#section-5.1 states that 70 characters is a reasonable limit.
        public static string GetBoundary(MediaTypeHeaderValue contentType, int lengthLimit)
        {
            var boundary = HeaderUtilities.RemoveQuotes(contentType.Boundary).Value;

            if (string.IsNullOrWhiteSpace(boundary))
            {
                throw new InvalidDataException("Missing content-type boundary.");
            }

            if (boundary.Length > lengthLimit)
            {
                throw new InvalidDataException(
                    $"Multipart boundary length limit {lengthLimit} exceeded.");
            }

            return boundary;
        }

        public static bool IsMultipartContentType(string contentType)
        {
            return !string.IsNullOrEmpty(contentType)
                   && contentType.IndexOf("multipart/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool HasFileContentDisposition(ContentDispositionHeaderValue contentDisposition)
        {
            // Content-Disposition: form-data; name="myfile1"; filename="Misc 002.jpg"
            return contentDisposition != null
                   && contentDisposition.DispositionType.Equals("form-data")
                   && (!string.IsNullOrEmpty(contentDisposition.FileName.Value)
                       || !string.IsNullOrEmpty(contentDisposition.FileNameStar.Value));
        }
    }
}
