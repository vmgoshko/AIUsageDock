using System.Text.Json;

namespace AIUsageDock;

internal sealed record CodexAccount(int Slot, string? Label = null, string Service = "codex")
{
    public string Id => Service == "codex" && Slot == 1 ? "codex" : $"{Service}{Slot}";
    public string Name => string.IsNullOrWhiteSpace(Label) ? $"{(Service == "claude" ? "Claude" : "Codex")} {Slot}" : Label;
}

internal sealed class AccountCatalog
{
    private sealed record State(int NextSlot, CodexAccount[] Accounts);
    private readonly string _path;
    private readonly object _gate = new();
    private State _state;
    public event EventHandler? Changed;

    public AccountCatalog(string path, bool preserveLegacyProfiles = false)
    {
        _path = path;
        if (File.Exists(path))
        {
            // Do not overwrite corrupt metadata and orphan existing logins.
            _state = JsonSerializer.Deserialize<State>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Account list is empty or invalid");
            if (_state.NextSlot < 1 || _state.Accounts.Any(a => a.Slot < 1 || a.Slot >= _state.NextSlot || a.Service is not ("codex" or "claude"))
                || _state.Accounts.Select(a => a.Slot).Distinct().Count() != _state.Accounts.Length)
                throw new InvalidDataException("Invalid account identifiers");
        }
        else
        {
            _state = preserveLegacyProfiles
                ? new State(3, [new(1), new(2)]) : new State(2, [new(1)]);
            Persist(_state);
        }
    }

    public static string DefaultPath => Path.Combine(CodexProfiles.Root, "accounts.json");
    public CodexAccount[] Accounts { get { lock (_gate) return _state.Accounts.ToArray(); } }

    public CodexAccount Add(string service = "codex")
    {
        if (service is not ("codex" or "claude")) throw new ArgumentException("Unknown service");
        CodexAccount account;
        lock (_gate)
        {
            account = new(_state.NextSlot, Service: service);
            var next = new State(checked(_state.NextSlot + 1), [.. _state.Accounts, account]);
            Persist(next);
            _state = next;
        }
        Changed?.Invoke(this, EventArgs.Empty);
        return account;
    }

    public void Remove(int slot)
    {
        lock (_gate)
        {
            var next = _state with { Accounts = _state.Accounts.Where(a => a.Slot != slot).ToArray() };
            Persist(next);
            _state = next;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public void Rename(int slot, string name)
    {
        name = name.Trim();
        if (name.Length is < 1 or > 60 || name.Any(char.IsControl))
            throw new ArgumentException("Enter a name of 1–60 characters");
        lock (_gate)
        {
            if (!_state.Accounts.Any(a => a.Slot == slot)) throw new InvalidOperationException("Band no longer exists");
            var next = _state with { Accounts = _state.Accounts.Select(a => a.Slot == slot ? a with { Label = name } : a).ToArray() };
            Persist(next);
            _state = next;
        }
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void Persist(State state)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(_path))!);
        var temporary = _path + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, _path, overwrite: true);
    }
}
