IF OBJECT_ID('dbo.OrderDrawings','U') IS NOT NULL DROP TABLE dbo.OrderDrawings;
IF OBJECT_ID('dbo.Orders','U') IS NOT NULL DROP TABLE dbo.Orders;

IF OBJECT_ID('dbo.ProcessRouteSteps','U') IS NOT NULL DROP TABLE dbo.ProcessRouteSteps;
IF OBJECT_ID('dbo.ProcessRoutes','U') IS NOT NULL DROP TABLE dbo.ProcessRoutes;

IF OBJECT_ID('dbo.Employees','U') IS NOT NULL DROP TABLE dbo.Employees;
IF OBJECT_ID('dbo.Departments','U') IS NOT NULL DROP TABLE dbo.Departments;

IF OBJECT_ID('dbo.Files','U') IS NOT NULL DROP TABLE dbo.Files;

CREATE TABLE dbo.Files
(
  Id uniqueidentifier NOT NULL CONSTRAINT PK_Files PRIMARY KEY,
  OriginalFileName nvarchar(512) NOT NULL,
  StoredRelativePath nvarchar(512) NOT NULL,
  ContentType nvarchar(128) NOT NULL,
  SizeBytes bigint NOT NULL,
  Sha256 char(64) NOT NULL,
  CreatedAtUtc datetimeoffset NOT NULL
);
CREATE INDEX IX_Files_CreatedAtUtc ON dbo.Files(CreatedAtUtc DESC);

CREATE TABLE dbo.Departments
(
  DepartmentId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Departments PRIMARY KEY,
  DepartmentName nvarchar(100) NOT NULL,
  CreatedAtUtc datetimeoffset NOT NULL CONSTRAINT DF_Departments_CreatedAtUtc DEFAULT (sysutcdatetime())
);
CREATE UNIQUE INDEX UX_Departments_DepartmentName ON dbo.Departments(DepartmentName);

CREATE TABLE dbo.Employees
(
  EmployeeId uniqueidentifier NOT NULL CONSTRAINT PK_Employees PRIMARY KEY,
  UserName nvarchar(120) NOT NULL,
  PasswordHash nvarchar(300) NOT NULL,
  DepartmentId int NOT NULL,
  AvatarFileId uniqueidentifier NULL,
  IsActive bit NOT NULL CONSTRAINT DF_Employees_IsActive DEFAULT (1),
  CreatedAtUtc datetimeoffset NOT NULL CONSTRAINT DF_Employees_CreatedAtUtc DEFAULT (sysutcdatetime())
);
CREATE UNIQUE INDEX UX_Employees_UserName ON dbo.Employees(UserName);
CREATE INDEX IX_Employees_DepartmentId ON dbo.Employees(DepartmentId);
ALTER TABLE dbo.Employees ADD CONSTRAINT FK_Employees_Departments
  FOREIGN KEY(DepartmentId) REFERENCES dbo.Departments(DepartmentId);

CREATE TABLE dbo.ProcessRoutes
(
  RouteId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessRoutes PRIMARY KEY,
  RouteName nvarchar(120) NOT NULL,
  Description nvarchar(500) NULL,
  IsActive bit NOT NULL CONSTRAINT DF_ProcessRoutes_IsActive DEFAULT(1),
  CreatedAtUtc datetimeoffset NOT NULL CONSTRAINT DF_ProcessRoutes_CreatedAtUtc DEFAULT(sysutcdatetime())
);
CREATE UNIQUE INDEX UX_ProcessRoutes_RouteName ON dbo.ProcessRoutes(RouteName);

CREATE TABLE dbo.ProcessRouteSteps
(
  RouteStepId int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ProcessRouteSteps PRIMARY KEY,
  RouteId int NOT NULL,
  StepSeq int NOT NULL,
  StepName nvarchar(120) NOT NULL,
  PrepMinutes int NOT NULL,
  ProcessMinutesPerUnit decimal(18,4) NOT NULL,
  IsActive bit NOT NULL CONSTRAINT DF_ProcessRouteSteps_IsActive DEFAULT(1),
  CreatedAtUtc datetimeoffset NOT NULL CONSTRAINT DF_ProcessRouteSteps_CreatedAtUtc DEFAULT(sysutcdatetime())
);
CREATE UNIQUE INDEX UX_ProcessRouteSteps_Route_StepSeq ON dbo.ProcessRouteSteps(RouteId, StepSeq);
ALTER TABLE dbo.ProcessRouteSteps ADD CONSTRAINT FK_ProcessRouteSteps_ProcessRoutes
  FOREIGN KEY(RouteId) REFERENCES dbo.ProcessRoutes(RouteId);
ALTER TABLE dbo.ProcessRouteSteps ADD CONSTRAINT CK_ProcessRouteSteps_Time CHECK (PrepMinutes >= 0 AND ProcessMinutesPerUnit >= 0);

CREATE TABLE dbo.Orders
(
  OrderId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_Orders PRIMARY KEY,
  OrderNo nvarchar(50) NOT NULL,
  GeneratedAt datetime2 NOT NULL,
  DueAt datetime2 NULL,
  CreatedAtUtc datetimeoffset NOT NULL CONSTRAINT DF_Orders_CreatedAtUtc DEFAULT(sysutcdatetime())
);
CREATE UNIQUE INDEX UX_Orders_OrderNo ON dbo.Orders(OrderNo);
CREATE INDEX IX_Orders_GeneratedAt ON dbo.Orders(GeneratedAt);

CREATE TABLE dbo.OrderDrawings
(
  OrderDrawingId bigint IDENTITY(1,1) NOT NULL CONSTRAINT PK_OrderDrawings PRIMARY KEY,
  OrderId bigint NOT NULL,
  DrawingNo nvarchar(120) NOT NULL,
  RouteId int NOT NULL,
  PlannedQty int NOT NULL,
  Difficulty decimal(18,3) NOT NULL CONSTRAINT DF_OrderDrawings_Difficulty DEFAULT(1.000),
  CreatedAtUtc datetimeoffset NOT NULL CONSTRAINT DF_OrderDrawings_CreatedAtUtc DEFAULT(sysutcdatetime())
);
CREATE UNIQUE INDEX UX_OrderDrawings_Order_Drawing ON dbo.OrderDrawings(OrderId, DrawingNo);

ALTER TABLE dbo.OrderDrawings ADD CONSTRAINT FK_OrderDrawings_Orders
  FOREIGN KEY(OrderId) REFERENCES dbo.Orders(OrderId);

ALTER TABLE dbo.OrderDrawings ADD CONSTRAINT FK_OrderDrawings_ProcessRoutes
  FOREIGN KEY(RouteId) REFERENCES dbo.ProcessRoutes(RouteId);

ALTER TABLE dbo.OrderDrawings ADD CONSTRAINT CK_OrderDrawings_Values CHECK (PlannedQty > 0 AND Difficulty > 0);
