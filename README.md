Entities - 

Tenant Entities
	Tenant
		Name, Slug (unique URL prefix), SubscriptionPlan, Status, DatabaseConnectionString (optional), ThemeSettings (JSON).
	User
		Email, IdentityGuid (from ASP.NET Identity), FirstName, LastName, RoleId, AvatarUrl
	Role

Core CRM Entities -> All Extending BaseTenantEntity
	Contact
		Name, Mobile, Email
	Lead
		FirstName, LastName, Mobile, Email, LeadSouce, ConvertdAccountId,ConvertedContactId, ConvertedOpportunityId, IsConverted
	Opportunity
		Title, ContactId, ExpectedCloseDate, Stage, LossReason

Activity & Communication
	Activity/Task
		Subject, Type (Call, Email, Meeting), DueDate, Status, Priority, Description, OwnerId (User FK), RelatedEntityType, RelatedEntityId
	Note
		Content, RelatedEntityType, RelatedEntityId

Sales & Product Enitites
	Product
		Name, SKU, Description, BasePrice, IsActive	

