using MeiErp.Platform.Kernel;
using Microsoft.EntityFrameworkCore;

namespace MeiErp.Modules.Inventory;

public class ProductFamily:AuditableEntity
{
    public string Name{get;set;}="";public string? Category{get;set;}public string? SkuPrefix{get;set;}public string? Description{get;set;}public bool IsActive{get;set;}=true;public List<Item> Items{get;set;}=[];
}
public enum InventoryItemKind{Model,Accessory}
public interface IProductHierarchyService
{
    Task<List<ProductFamily>> ListAsync(string? search=null,CancellationToken ct=default);Task<ProductFamily> SaveAsync(ProductFamily family,CancellationToken ct=default);Task DeleteAsync(int id,CancellationToken ct=default);
}
public sealed class ProductHierarchyService(InventoryDbContext db):IProductHierarchyService
{
    public async Task<List<ProductFamily>> ListAsync(string? search=null,CancellationToken ct=default){var q=db.ProductFamilies.AsNoTracking().Include(x=>x.Items).AsQueryable();if(!string.IsNullOrWhiteSpace(search))q=q.Where(x=>EF.Functions.ILike(x.Name,$"%{search.Trim()}%")||x.Items.Any(i=>EF.Functions.ILike(i.Name,$"%{search.Trim()}%")));return await q.OrderBy(x=>x.Name).ToListAsync(ct);}
    public async Task<ProductFamily> SaveAsync(ProductFamily family,CancellationToken ct=default){if(string.IsNullOrWhiteSpace(family.Name))throw new InvalidOperationException("Product family name is required.");if(family.Id==0)db.Add(family);else{var row=await db.ProductFamilies.FindAsync([family.Id],ct)??throw new InvalidOperationException("Product family not found.");row.Name=family.Name.Trim();row.Category=family.Category;row.SkuPrefix=family.SkuPrefix;row.Description=family.Description;row.IsActive=family.IsActive;family=row;}await db.SaveChangesAsync(ct);return family;}
    public async Task DeleteAsync(int id,CancellationToken ct=default){var row=await db.ProductFamilies.Include(x=>x.Items).FirstOrDefaultAsync(x=>x.Id==id,ct);if(row is null)return;if(row.Items.Any(x=>x.QuantityOnHand!=0))throw new InvalidOperationException("This family still contains stock. Move it out before deleting.");foreach(var item in row.Items)item.ProductFamilyId=null;db.Remove(row);await db.SaveChangesAsync(ct);}
}
