using System.Globalization;
using CatalogoMaui.Models;
using CatalogoMaui.Services;
namespace CatalogoMaui.Views;

public partial class ProductoDetallePage : ContentPage, IQueryAttributable
{
    private int id;
    private string fotoGuardada = "";   // foto que tiene el producto en SQLite
    private string? fotoTemporal;       // copia temporal elegida de la galería (aún sin guardar)

    public ProductoDetallePage()
    {
        InitializeComponent();
        Title = "Agregar producto";
        Foto.ItemsSource = new List<string> { "audif.jpg", "bocina.jpg", "cuaderno.png", "mochila.png", "mouse.jpg", "teclado.jpg", "termo.png" };
        Foto.SelectedIndex = 0;
    }

    // Con datos es edición. Sin datos conservamos el formulario nuevo.
    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (!query.TryGetValue("producto", out var dato) || dato is not Producto p)
            return;

        id = p.Id;
        fotoGuardada = p.Foto;
        Title = "Editar producto";
        Nombre.Text = p.Nombre;
        Descripcion.Text = p.Descripcion;
        Detalle.Text = p.Detalle;
        Precio.Text = p.Precio.ToString("0.00", CultureInfo.CurrentCulture);

        if (FotosProducto.EsFotoPropia(p.Foto))
        {
            Foto.SelectedIndex = -1;
            VistaFoto.Source = FotosProducto.Resolver(p.Foto);
        }
        else
        {
            Foto.SelectedItem = p.Foto;
        }
        Eliminar.IsVisible = true;
    }

    private void AlCambiarFoto(object? sender, EventArgs e)
    {
        if (Foto.SelectedItem is string archivo)
        {
            // Elegir una imagen incluida descarta la foto de la galería pendiente.
            FotosProducto.LimpiarTemporal(fotoTemporal);
            fotoTemporal = null;
            VistaFoto.Source = archivo;
        }
    }

    private async void AlElegirFoto(object? sender, EventArgs e)
    {
        ElegirFoto.IsEnabled = false;
        try
        {
            var temporal = await FotosProducto.ElegirAsync();
            if (temporal is null) return; // cancelado: todo queda igual

            FotosProducto.LimpiarTemporal(fotoTemporal);
            fotoTemporal = temporal;
            Foto.SelectedIndex = -1;
            VistaFoto.Source = temporal;
        }
        catch (Exception ex)
        {
            await DisplayAlert("No se pudo elegir la foto", ex.Message, "Aceptar");
        }
        finally
        {
            ElegirFoto.IsEnabled = true;
        }
    }

    private async void AlGuardar(object? sender, EventArgs e)
    {
        if (!Guardar.IsEnabled) return;

        var textoPrecio = Precio.Text?.Trim().Replace(',', '.');
        bool hayFoto = fotoTemporal is not null || Foto.SelectedItem is string ||
                       FotosProducto.EsFotoPropia(fotoGuardada);

        if (string.IsNullOrWhiteSpace(Nombre.Text) ||
            string.IsNullOrWhiteSpace(Descripcion.Text) ||
            string.IsNullOrWhiteSpace(Detalle.Text) ||
            !decimal.TryParse(textoPrecio, NumberStyles.AllowDecimalPoint,
                CultureInfo.InvariantCulture, out var importe) ||
            importe < 0 || importe > 99999999.99m ||
            decimal.Round(importe, 2) != importe ||
            !hayFoto)
        {
            await DisplayAlert("Revisa los datos",
                "Completa los campos y escribe un precio válido de hasta dos decimales.", "Aceptar");
            return;
        }

        var producto = new Producto
        {
            Id = id, Nombre = Nombre.Text.Trim(), Descripcion = Descripcion.Text.Trim(),
            Detalle = Detalle.Text.Trim(), Precio = importe
        };
        string? incluida = Foto.SelectedItem as string;
        string anterior = fotoGuardada;

        await EjecutarAsync(async () =>
        {
            string? fotoNueva = null;
            // 1) copiar la foto nueva (si hay)
            if (fotoTemporal is not null)
            {
                fotoNueva = await FotosProducto.GuardarAsync(fotoTemporal);
                producto.Foto = fotoNueva;
            }
            else
            {
                producto.Foto = incluida ?? anterior; // imagen incluida o conservar la propia
            }

            // 2) guardar en SQLite; si falla, borrar la copia nueva y conservar la anterior
            try
            {
                if (id == 0)
                    await ProductosSqlite.Instancia.Insertar(producto);
                else if (!await ProductosSqlite.Instancia.Actualizar(producto))
                    throw new InvalidOperationException("El producto ya no existe.");
            }
            catch
            {
                FotosProducto.EliminarPropia(fotoNueva);
                throw;
            }

            // 3) solo si se guardó, retirar la foto propia anterior
            if (anterior != producto.Foto)
                FotosProducto.EliminarPropia(anterior);
            FotosProducto.LimpiarTemporal(fotoTemporal);
            fotoTemporal = null;
            return true;
        });
    }

    private async void AlEliminar(object? sender, EventArgs e)
    {
        if (!Eliminar.IsEnabled || id == 0) return;
        await EjecutarAsync(async () =>
        {
            if (!await DisplayAlert("Eliminar producto", "¿Deseas eliminar este producto?",
                "Eliminar", "Cancelar"))
                return false;
            if (!await ProductosSqlite.Instancia.Eliminar(id))
                throw new InvalidOperationException("El producto ya no existe.");
            FotosProducto.EliminarPropia(fotoGuardada);
            FotosProducto.LimpiarTemporal(fotoTemporal);
            return true;
        });
    }

    private async Task EjecutarAsync(Func<Task<bool>> operacion)
    {
        Guardar.IsEnabled = Eliminar.IsEnabled = ElegirFoto.IsEnabled = false;
        try
        {
            if (await operacion()) await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            await DisplayAlert("No se completó la operación", ex.Message, "Aceptar");
        }
        finally
        {
            Guardar.IsEnabled = Eliminar.IsEnabled = ElegirFoto.IsEnabled = true;
        }
    }
}
