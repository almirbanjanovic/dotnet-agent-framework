using Contoso.BlazorUi.Models;

namespace Contoso.BlazorUi.Services;

/// <summary>
/// Browser-session shopping cart. Lives in WASM memory only — refreshing
/// the page (or signing out) wipes it. That mirrors how other lab demos
/// behave: the agent backend is the source of truth, the cart is purely
/// pre-checkout staging.
/// </summary>
public sealed class CartService
{
    private readonly List<CartLine> _lines = new();

    public event Action? Changed;

    public IReadOnlyList<CartLine> Lines => _lines;

    public int ItemCount => _lines.Sum(l => l.Quantity);

    public decimal Subtotal => _lines.Sum(l => l.Quantity * l.UnitPrice);

    public bool IsEmpty => _lines.Count == 0;

    public void Add(Product product, int quantity = 1)
    {
        if (product is null || quantity <= 0)
        {
            return;
        }

        var existing = _lines.FirstOrDefault(l => l.ProductId == product.Id);
        if (existing is not null)
        {
            existing.Quantity += quantity;
        }
        else
        {
            _lines.Add(new CartLine
            {
                ProductId = product.Id,
                ProductName = product.Name,
                UnitPrice = product.Price,
                ImageFilename = product.ImageFilename,
                Quantity = quantity
            });
        }

        Changed?.Invoke();
    }

    public void SetQuantity(string productId, int quantity)
    {
        var line = _lines.FirstOrDefault(l => l.ProductId == productId);
        if (line is null)
        {
            return;
        }

        if (quantity <= 0)
        {
            _lines.Remove(line);
        }
        else
        {
            line.Quantity = quantity;
        }

        Changed?.Invoke();
    }

    public void Remove(string productId)
    {
        var line = _lines.FirstOrDefault(l => l.ProductId == productId);
        if (line is null)
        {
            return;
        }

        _lines.Remove(line);
        Changed?.Invoke();
    }

    public void Clear()
    {
        if (_lines.Count == 0)
        {
            return;
        }

        _lines.Clear();
        Changed?.Invoke();
    }
}

public sealed class CartLine
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public string ImageFilename { get; set; } = string.Empty;
    public decimal UnitPrice { get; set; }
    public int Quantity { get; set; }
}
