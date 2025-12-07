CREATE   PROCEDURE [infra].[Inbox_Cleanup]
    @RetentionSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoffTime DATETIMEOFFSET(3) = DATEADD(SECOND, -@RetentionSeconds, SYSDATETIMEOFFSET());

    DELETE FROM [infra].[Inbox]
     WHERE Status = 'Done'
       AND ProcessedUtc IS NOT NULL
       AND ProcessedUtc < @cutoffTime;

    SELECT @@ROWCOUNT AS DeletedCount;
END