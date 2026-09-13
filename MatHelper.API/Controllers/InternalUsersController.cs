using MatHelper.API.Common;
using MatHelper.BLL.Interfaces;
using MatHelper.CORE.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MatHelper.API.Controllers
{
    [Authorize(Roles = "Admin, Owner, Service")]
    [ApiController]
    [Route("api/v1/internal/users")]
    public class InternalUsersController : ControllerBase
    {
        private readonly IUserManagementService _userManagementService;
        private readonly ITokenService _tokenService;
        private readonly ILogger<InternalUsersController> _logger;

        public InternalUsersController(
            IUserManagementService userManagementService,
            ITokenService tokenService,
            ILogger<InternalUsersController> logger)
        {
            _userManagementService = userManagementService;
            _tokenService = tokenService;
            _logger = logger;
        }

        /// <summary>
        /// Retrieves authoritative user information for internal service-to-service operations (e.g., gmhelper-notify-api).
        /// </summary>
        /// <param name="id">The unique identifier of the user.</param>
        /// <returns>Authoritative internal user profile DTO.</returns>
        [HttpGet("{id}")]
        [ProducesResponseType(typeof(ApiResponse<InternalUserDto>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status403Forbidden)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status404NotFound)]
        public async Task<IActionResult> GetUserById(string id)
        {
            try
            {
                if (!User.IsInRole("Service"))
                {
                    var adminValidation = await AdminValidation.ValidateAdminAsync(this, _tokenService);
                    if (adminValidation != null) return adminValidation;
                }

                if (string.IsNullOrWhiteSpace(id) || !Guid.TryParse(id, out var parsedUserId) || parsedUserId == Guid.Empty)
                {
                    return NotFound(ApiResponse<string>.Fail("User not found."));
                }

                var user = await _userManagementService.GetInternalUserByIdAsync(parsedUserId);
                if (user == null)
                {
                    return NotFound(ApiResponse<string>.Fail("User not found."));
                }

                return Ok(ApiResponse<InternalUserDto>.Ok(user));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while resolving internal user with ID: {UserId}", id);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("Internal server error."));
            }
        }

        /// <summary>
        /// Searches users by username or email for internal service-to-service operations (e.g., gmhelper-notify-api).
        /// </summary>
        /// <param name="query">The search term for username or email.</param>
        /// <param name="limit">Optional maximum number of results to return (default 20, max 50).</param>
        /// <returns>List of matching internal user profile DTOs.</returns>
        [HttpGet("search")]
        [ProducesResponseType(typeof(ApiResponse<List<InternalUserDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> SearchUsers([FromQuery] string? query, [FromQuery] int limit = 20)
        {
            try
            {
                if (!User.IsInRole("Service"))
                {
                    var adminValidation = await AdminValidation.ValidateAdminAsync(this, _tokenService);
                    if (adminValidation != null) return adminValidation;
                }

                if (string.IsNullOrWhiteSpace(query))
                {
                    return BadRequest(ApiResponse<string>.Fail("Search query is required."));
                }

                var users = await _userManagementService.SearchInternalUsersAsync(query, limit);
                return Ok(ApiResponse<List<InternalUserDto>>.Ok(users));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while searching internal users with query: {Query}", query);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("Internal server error."));
            }
        }
    }
}
