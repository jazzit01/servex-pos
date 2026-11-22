using System;
using System.IO;

namespace ServeX.Services
{
    public sealed class RememberedUsername
    {
        private readonly string _path;
        private readonly LogService _log;

        public RememberedUsername(LogService log, string path = null)
        {
            _log = log;
            _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ServeX", "remembered-username.txt");
        }

        public string Read()
        {
            try
            {
                return File.Exists(_path) ? File.ReadAllText(_path).Trim() : string.Empty;
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _log.LogError("Could not read remembered username", exception);
                return string.Empty;
            }
        }

        public void Save(string username)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username))
                {
                    File.Delete(_path);
                    return;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(_path));
                File.WriteAllText(_path, username);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                _log.LogError("Could not update remembered username", exception);
            }
        }
    }
}
