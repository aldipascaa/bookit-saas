using System.ComponentModel.DataAnnotations;

namespace BookIt.Api.Application.Contracts;

public record RescheduleRequest([Required] DateTime StartTime);
