using Samples.HelloCart.V2;
using ActualLab.Fusion.EntityFramework;

namespace Samples.HelloCart.V3;

public class DbProductServiceUsingEntityResolver(
    DbHub<AppDbContext> dbHub,
    IDbEntityResolver<string, DbProduct> productResolver
    ) : IProductService
{
    public virtual async Task Edit(EditCommand<Product> command, CancellationToken cancellationToken = default)
    {
        var (productId, product) = command;
        if (string.IsNullOrEmpty(productId))
            throw new ArgumentOutOfRangeException(nameof(command));

        if (Computed.IsInvalidating()) {
            _ = Get(productId, default);
            return;
        }

        await using var dbContext = await dbHub.CreateCommandDbContext(cancellationToken);
        var dbProduct = await dbContext.Products.FindAsync(DbKey.Compose(productId), cancellationToken);
        if (product == null) {
            if (dbProduct != null)
                dbContext.Remove(dbProduct);
        }
        else {
            if (dbProduct != null)
                dbProduct.Price = product.Price;
            else
                dbContext.Add(new DbProduct { Id = productId, Price = product.Price });
        }
        await dbContext.SaveChangesAsync(cancellationToken);

        // Adding LogMessageCommand as event
        var context = CommandContext.GetCurrent();
        var message = product == null
            ? $"Product removed: {productId}"
            : $"Product updated: {productId} with Price = {product.Price}";
        context.Operation.AddEvent(new LogMessageCommand(Ulid.NewUlid().ToString(), message));
        context.Operation.AddEvent(LogMessageCommand.NewDelayed());
    }

    public virtual async Task<Product?> Get(string id, CancellationToken cancellationToken = default)
    {
        var dbProduct = await productResolver.Get(id, cancellationToken);
        return dbProduct == null ? null : new Product(dbProduct.Id, dbProduct.Price);
    }
}
