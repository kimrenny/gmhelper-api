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
        /// Retrieves a paginated list of internal users for machine-to-machine operations (e.g., gmhelper-notify-api campaign audience resolution).
        /// </summary>
        /// <param name="page">Page number (1-based, default 1).</param>
        /// <param name="pageSize">Number of users per page (default 50, max 250).</param>
        /// <param name="activeOnly">Whether to return only active users (default true).</param>
        /// <param name="unblockedOnly">Whether to return only unblocked users (default true).</param>
        /// <returns>Paged list of internal user DTOs.</returns>
        [HttpGet]
        [ProducesResponseType(typeof(ApiResponse<PagedResult<InternalUserDto>>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(ApiResponse<string>), StatusCodes.Status403Forbidden)]
        public async Task<IActionResult> GetUsers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] bool activeOnly = true,
            [FromQuery] bool unblockedOnly = true)
        {
            try
            {
                if (!User.IsInRole("Service"))
                {
                    var adminValidation = await AdminValidation.ValidateAdminAsync(this, _tokenService);
                    if (adminValidation != null) return adminValidation;
                }

                if (page < 1)
                {
                    return BadRequest(ApiResponse<string>.Fail("Page must be greater than or equal to 1."));
                }

                if (pageSize < 1)
                {
                    return BadRequest(ApiResponse<string>.Fail("Page size must be greater than or equal to 1."));
                }

                var effectivePageSize = Math.Min(pageSize, 250);

                var result = await _userManagementService.GetInternalUsersPagedAsync(page, effectivePageSize, activeOnly, unblockedOnly);
                return Ok(ApiResponse<PagedResult<InternalUserDto>>.Ok(result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while listing internal users (page {Page}, pageSize {PageSize})", page, pageSize);
                return StatusCode(StatusCodes.Status500InternalServerError, ApiResponse<string>.Fail("Internal server error."));
            }
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
