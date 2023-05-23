namespace IronMonkey.Api.Infrastructures.Tenants;
public class TenantService : ITenantGetter, ITenantSetter
{
    public Tenant Tenant { get; private set; } = default!;

    public void SetTenant(Tenant tenant)
    {
        Tenant = tenant;
    }
}

public interface ITenantGetter 
{
    Tenant Tenant { get; }
}

public interface ITenantSetter
{
    void SetTenant(Tenant key);
}