namespace AlgoMCPServer.Strategies {
    public interface IStrategy : IDisposable {
        Task Run();
    }
}
