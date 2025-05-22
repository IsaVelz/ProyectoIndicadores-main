using static System.Runtime.InteropServices.JavaScript.JSType;

namespace PIndicadores.Models
{
    public class Indicadores
    {
        public int id { get; set; }
        public string? codigo { get; set; }
        public string? nombre { get; set; }
        public string? objetivo { get; set; }
        public string? alcance { get; set; }
        public string? formula { get; set; }
        public int fkidtipoindicador { get; set; }
        public int fkidunidadmedicion { get; set; }
        public string? meta { get; set; }
        public int fkidsentido { get; set; }
        public int fkidfrecuencia { get; set; }
        public int fkidarticulo { get; set; }
        public int fkidliteral { get; set; }
        public int fkidnumeral { get; set; }
        public int fkidparagrafo { get; set; }
    }
}
