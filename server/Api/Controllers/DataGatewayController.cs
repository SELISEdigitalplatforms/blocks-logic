//using Blocks.Genesis;
//using DataGateway.DomainService.Models;
//using DataGateway.Driver;
//using Microsoft.AspNetCore.Authorization;
//using Microsoft.AspNetCore.Mvc;
//using System.Net;

//namespace Api.Controllers
//{
//    [Route("schemas")]
//    [ApiController]
//    public class DataGatewayController : ControllerBase
//    {
//        private readonly IDataGatewayDriverService _dataGatewayDriver;

//        public DataGatewayController(
//            IDataGatewayDriverService dataGatewayDriver)
//        {
//            _dataGatewayDriver = dataGatewayDriver;
//        }

//        #region Schema Management

//        /// <summary>
//        /// Creates a new schema.
//        /// </summary>
//        /// <param name="request">The schema creation request.</param>
//        /// <returns>Returns the result of the schema creation.</returns>
//        [Authorize]
//        [HttpPost("create-schema")]
//        [ProducesResponseType(StatusCodes.Status200OK)]
//        [ProducesResponseType(StatusCodes.Status400BadRequest)]
//        public async Task<IActionResult> CreateSchemaAsync([FromBody] CreateSchemaRequest request)
//        {
//            if (string.IsNullOrWhiteSpace(request.ProjectKey))
//                return StatusCode((int)HttpStatusCode.BadRequest, new { Message = "INVALID_PROJECT_KEY" });

//            var response = await _dataGatewayDriver.CreateSchemaAsync(request);
//            return StatusCode(response.HttpStatusCode, response);
//        }

//        /// <summary>
//        /// Updates an existing schema.
//        /// </summary>
//        /// <param name="request">The schema update request.</param>
//        /// <returns>Returns the result of the schema update.</returns>
//        [Authorize]
//        [HttpPut("update-schema")]
//        [ProducesResponseType(StatusCodes.Status200OK)]
//        [ProducesResponseType(StatusCodes.Status400BadRequest)]
//        public async Task<IActionResult> UpdateSchemaAsync([FromBody] UpdateSchemaRequest request)
//        {
//            if (string.IsNullOrWhiteSpace(request.ProjectKey))
//                return StatusCode((int)HttpStatusCode.BadRequest, new { Message = "INVALID_PROJECT_KEY" });

//            var response = await _dataGatewayDriver.UpdateSchemaAsync(request);
//            return StatusCode(response.HttpStatusCode, response);
//        }

//        /// <summary>
//        /// Deletes a schema by its unique ID.
//        /// </summary>
//        /// <param name="id">The unique identifier of the schema to delete.</param>
//        /// <param name="projectKey">The unique identifier of the project.</param>
//        /// <returns>Returns the result of the schema deletion.</returns>
//        [Authorize]
//        [HttpDelete("delete-schema")]
//        [ProducesResponseType(StatusCodes.Status200OK)]
//        [ProducesResponseType(StatusCodes.Status400BadRequest)]
//        public async Task<IActionResult> DeleteSchemaAsync([FromQuery] string id, string projectKey = "")
//        {
//            if (string.IsNullOrWhiteSpace(id))
//                return StatusCode((int)HttpStatusCode.BadRequest, new { Message = "INVALID_SCHEMA_ID" });

//            var response = await _dataGatewayDriver.DeleteSchemaAsync(id);
//            return StatusCode(response.HttpStatusCode, response);
//        }

//        #endregion

//        #region Schema Definition Management

//        /// <summary>
//        /// Creates a new schema definition with field specifications.
//        /// </summary>
//        /// <param name="request">The schema definition creation request.</param>
//        /// <returns>Returns the result of the schema definition creation.</returns>
//        [Authorize]
//        [HttpPost("create-schema-definition")]
//        [ProducesResponseType(StatusCodes.Status200OK)]
//        [ProducesResponseType(StatusCodes.Status400BadRequest)]
//        public async Task<IActionResult> CreateSchemaDefinitionAsync([FromBody] CreateSchemaDefinitionRequest request)
//        {
//            if (string.IsNullOrWhiteSpace(request.ProjectKey))
//                return StatusCode((int)HttpStatusCode.BadRequest, new { Message = "INVALID_PROJECT_KEY" });

//            var response = await _dataGatewayDriver.CreateSchemaDefinitionAsync(request);
//            return StatusCode(response.HttpStatusCode, response);
//        }

//        /// <summary>
//        /// Updates an existing schema definition.
//        /// </summary>
//        /// <param name="request">The schema definition update request.</param>
//        /// <returns>Returns the result of the schema definition update.</returns>
//        [Authorize]
//        [HttpPut("update-schema-definition")]
//        [ProducesResponseType(StatusCodes.Status200OK)]
//        [ProducesResponseType(StatusCodes.Status400BadRequest)]
//        public async Task<IActionResult> UpdateSchemaDefinitionAsync([FromBody] UpdateSchemaDefinitionRequest request)
//        {
//            if (string.IsNullOrWhiteSpace(request.ProjectKey))
//                return StatusCode((int)HttpStatusCode.BadRequest, new { Message = "INVALID_PROJECT_KEY" });

//            var response = await _dataGatewayDriver.UpdateSchemaDefinitionAsync(request);
//            return StatusCode(response.HttpStatusCode, response);
//        }

//        /// <summary>
//        /// Saves or updates field definitions for an existing schema, with support for field deletion.
//        /// </summary>
//        /// <param name="request">The save field definition request.</param>
//        /// <returns>Returns the result of the field definition save operation.</returns>
//        [Authorize]
//        [HttpPost("save-field-definition")]
//        [ProducesResponseType(StatusCodes.Status200OK)]
//        [ProducesResponseType(StatusCodes.Status400BadRequest)]
//        public async Task<IActionResult> SaveFieldDefinitionAsync([FromBody] SaveFieldDefinitionRequest request)
//        {
//            if (string.IsNullOrWhiteSpace(request.SchemaDefinitionItemId))
//                return StatusCode((int)HttpStatusCode.BadRequest, new { Message = "INVALID_SCHEMA_DEFINITION_ID" });

//            var response = await _dataGatewayDriver.SaveFieldDefinitionAsync(request);
//            return StatusCode(response.HttpStatusCode, response);
//        }

//        #endregion

//        #region Schema Retrieval

//        /// <summary>
//        /// Retrieves the details of a specific schema definition by its unique ID. Use this endpoint to get the schema definition details, including its fields and type.
//        /// </summary>
//        /// <param name="id">The unique identifier of the schema definition to retrieve.</param>
//        /// <param name="projectKey">The unique identifier of the project to retrieve.</param>
//        /// <returns>Returns the schema definition details if found, or an error message if not found.</returns>
//        [Authorize]
//        [HttpGet("get-by-id")]
//        [ProducesResponseType(StatusCodes.Status200OK)]
//        [ProducesResponseType(StatusCodes.Status404NotFound)]
//        public async Task<IActionResult> GetSchemaDefinitionByIdAsync([FromQuery] string id, string projectKey = "")
//        {
//            if (string.IsNullOrWhiteSpace(id))
//                return StatusCode((int)HttpStatusCode.BadRequest, new { Message = "INVALID_SCHEMA_ID" });

//            var response = await _dataGatewayDriver.GetSchemaByIdAsync(id);
//            return StatusCode(response.HttpStatusCode, response);
//        }

//        /// <summary>
//        /// Retrieves a paginated list of all schema definitions with optional filtering and sorting.
//        /// </summary>
//        /// <param name="request">The pagination and filter parameters.</param>
//        /// <returns>Returns a paginated list of schema definitions.</returns>
//        [Authorize]
//        [HttpGet("get-all")]
//        [ProducesResponseType(StatusCodes.Status200OK)]
//        public async Task<IActionResult> GetAllSchemaDefinitionsAsync([FromQuery] GetSchemaDefinitionListRequest request)
//        {
//            var response = await _dataGatewayDriver.GetAllSchemasAsync(request);
//            return Ok(response);
//        }

//        #endregion
//    }

//    internal sealed class ProjectKeyModel : IProjectKey
//    {
//        public string ProjectKey { get; set; } = string.Empty;
//    }
//}
