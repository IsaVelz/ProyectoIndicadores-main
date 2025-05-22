namespace PIndicadores.Models
{
    public class GuardarIndicador
    {
        public Indicadores Indicador { get; set; }
        public List<Variablesporindicador> variablesporindicador { get; set; }
        public List<Responsablesporindicador> responsablesporindicador { get; set; }
        public List<Represenvisualporindicador> represenvisualporindicador { get; set; }
        public List<Resultadoindicador> resultadoindicador { get; set; }
    }
}

