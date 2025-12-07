/*
 * StringIdList - User-Defined Table Type
 * 
 * Purpose: Pass lists of string IDs to stored procedures efficiently
 * Usage: Used for Inbox operations with VARCHAR message IDs
 * 
 * Example:
 *   DECLARE @ids StringIdList;
 *   INSERT INTO @ids VALUES ('msg1'), ('msg2');
 *   EXEC Inbox_Ack @OwnerToken, @ids;
 */

CREATE TYPE [dbo].[StringIdList] AS TABLE
(
    [Id] VARCHAR(64) NOT NULL PRIMARY KEY
);
GO
