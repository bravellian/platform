/*
 * GuidIdList - User-Defined Table Type
 * 
 * Purpose: Pass lists of GUIDs to stored procedures efficiently
 * Usage: Used for batch operations (Ack, Abandon, Fail)
 * 
 * Example:
 *   DECLARE @ids GuidIdList;
 *   INSERT INTO @ids VALUES ('guid1'), ('guid2');
 *   EXEC Outbox_Ack @OwnerToken, @ids;
 */

CREATE TYPE [dbo].[GuidIdList] AS TABLE
(
    [Id] UNIQUEIDENTIFIER NOT NULL PRIMARY KEY
);
GO
