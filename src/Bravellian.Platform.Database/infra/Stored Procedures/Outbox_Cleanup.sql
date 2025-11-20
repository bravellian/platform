CREATE   PROCEDURE [infra].[Outbox_Cleanup]
    @RetentionSeconds INT
AS
BEGIN
    SET NOCOUNT ON;
    DECLARE @cutoffTime DATETIMEOFFSET = DATEADD(SECOND, -@RetentionSeconds, SYSDATETIMEOFFSET());
    
    DELETE FROM [infra].[Outbox]
     WHERE IsProcessed = 1
       AND ProcessedAt IS NOT NULL
       AND ProcessedAt < @cutoffTime;
       
    SELECT @@ROWCOUNT AS DeletedCount;
END