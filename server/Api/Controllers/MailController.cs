using Blocks.Genesis;
//using CloudConfiguration.DomainService.Mail.Entities;
//using CloudConfiguration.DomainService.Mail.RequestModel;
//using CloudConfiguration.DomainService.Shared.Services;
using Mail.DomainService.Mails;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BlocksTemplate.Api.Controllers
{
    /// <summary>
    /// Mail sending and mailbox reads, routed as <c>/api/Mail/{action}</c>. Failures surface as 400 with
    /// the service result as the body rather than as an exception.
    /// </summary>
    [ApiController]
    [Route("[controller]/[action]")]

    public class MailController : ControllerBase
    {
        //private readonly IConfigurationService _configurationService;
        private readonly IMailService _mailService;
        /// <summary>Takes the mail and configuration services.</summary>
        public MailController(
            //IConfigurationService configurationService,
            IMailService mailService )
        {
            //_configurationService = configurationService;
            _mailService = mailService;
            }

        

        #region Mail Send

        /// <summary>
        /// <c>POST</c> — sends a mail to addresses that need not be users of the project. Its own
        /// permission: <c>mail::manage</c> alone reaches only the project's users (<see cref="Send"/>).
        /// </summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::mail::send-any")]
        public async Task<IActionResult> SendToAny ( [FromBody] SendMailToAny request )
        {
            var result = await _mailService.ProcessMailToAnyAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary><c>POST</c> — sends a mail through the configured mail server.</summary>
        [HttpPost]
        [ProtectedEndPoint("blocks-logic::mail::manage")]
        public async Task<IActionResult> Send ( [FromBody] SendMail request )
        {
            var result = await _mailService.ProcessMailAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// <c>GET</c> — the messages in a synced mailbox. Anonymous, unlike its single-message twin
        /// <see cref="GetMailBoxMail"/>.
        /// </summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::mail::read")]
        public async Task<IActionResult> GetMailBoxMails ( [FromQuery] GetMailBoxMails request )
        {
            var result = await _mailService.GetMailBoxMailsAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary><c>GET</c> — one message from a synced mailbox.</summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-logic::mail::read")]
        public async Task<IActionResult> GetMailBoxMail ( [FromQuery] GetMailBoxMail request )
        {
            var result = await _mailService.GetMailBoxMailAsync(request);
            return result.IsSuccess ? Ok(result) : BadRequest(result);
        }

        /// <summary>
        /// <c>GET</c> — summaries of the configured mail servers. The request object is accepted for
        /// signature symmetry with the other reads and is deliberately unused.
        /// </summary>
        [HttpGet]
        [ProtectedEndPoint("blocks-os::mail::gets")]
        public async Task<IActionResult> Gets([FromQuery] GetMailConfigurationsRequest request)
        {
            _ = request;
            var result = await _mailService.GetMailServerConfigurationSummariesAsync();
            return Ok(result);
        }
        #endregion
        }
    }
