namespace DomainService.Workflow.Nodes.LogicIFV1
{
    public class LogicIfV1Parameter
    {
        public string ConditionType { get; set; } = "and";
        public List<Condition> Conditions { get; set; } = new List<Condition>();
    }

    public class Condition
    {

        public string Left { get; set; }
        public string Operator { get; set; }
        public string Right { get; set; }
        public string Type { get; set; }

    }
}