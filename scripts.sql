USE [PIAS]
GO

/****** Object:  Table [dbo].[EncryptedDataKey]    Script Date: 10/11/2024 1:54:47 AM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[EncryptedDataKey](
	[EncryptedDataKeyId] [int] IDENTITY(1,1) NOT NULL,
	[EncryptedKey] [varbinary](256) NOT NULL,
	[CreatedAtUTC] [datetime] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[EncryptedDataKeyId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO


USE [PIAS]
GO

/****** Object:  Table [dbo].[TransactionDataKeyMapping]    Script Date: 10/11/2024 1:55:31 AM ******/
SET ANSI_NULLS ON
GO

SET QUOTED_IDENTIFIER ON
GO

CREATE TABLE [dbo].[TransactionDataKeyMapping](
	[TransactionId] [uniqueidentifier] NOT NULL,
	[DataKeyId] [int] NOT NULL,
PRIMARY KEY CLUSTERED 
(
	[TransactionId] ASC
)WITH (PAD_INDEX = OFF, STATISTICS_NORECOMPUTE = OFF, IGNORE_DUP_KEY = OFF, ALLOW_ROW_LOCKS = ON, ALLOW_PAGE_LOCKS = ON, OPTIMIZE_FOR_SEQUENTIAL_KEY = OFF) ON [PRIMARY]
) ON [PRIMARY]
GO
