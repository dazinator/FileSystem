// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Primitives;

namespace Microsoft.AspNet.FileProviders
{
    /// <summary>
    /// Looks up files using embedded resources in the specified assembly.
    /// This file provider is case sensitive.
    /// </summary>
    public class EmbeddedFileProvider : IFileProvider
    {
        private readonly Assembly _assembly;
        private readonly string _baseNamespace;
        private readonly DateTimeOffset _lastModified;

        /// <summary>
        /// Initializes a new instance of the <see cref="EmbeddedFileProvider" /> class using the specified
        /// assembly and empty base namespace.
        /// </summary>
        /// <param name="assembly"></param>
        public EmbeddedFileProvider(Assembly assembly)
            : this(assembly, string.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="EmbeddedFileProvider" /> class using the specified
        /// assembly and base namespace.
        /// </summary>
        /// <param name="assembly">The assembly that contains the embedded resources.</param>
        /// <param name="baseNamespace">The base namespace that contains the embedded resources.</param>
        public EmbeddedFileProvider(Assembly assembly, string baseNamespace)
        {
            if (assembly == null)
            {
                throw new ArgumentNullException("assembly");
            }

            _baseNamespace = string.IsNullOrEmpty(baseNamespace) ? string.Empty : baseNamespace + ".";
            _assembly = assembly;
            // REVIEW: Does this even make sense?
            _lastModified = DateTimeOffset.MaxValue;
        }

        /// <summary>
        /// Locates a file at the given path.
        /// </summary>
        /// <param name="subpath">The path that identifies the file. </param>
        /// <returns>The file information. Caller must check Exists property.</returns>
        public IFileInfo GetFileInfo(string subpath)
        {
            if (string.IsNullOrEmpty(subpath))
            {
                return new NotFoundFileInfo(subpath);
            }

            var builder = new StringBuilder(_baseNamespace.Length + subpath.Length);
            builder.Append(_baseNamespace);
            builder.Append(subpath);

            for (var i = _baseNamespace.Length; i < builder.Length; i++)
            {
                if (builder[i] == '/' || builder[i] == '\\')
                {
                    builder[i] = '.';
                }
            }

            var resourcePath = builder.ToString();
            var name = Path.GetFileName(subpath);
            if (_assembly.GetManifestResourceInfo(resourcePath) == null)
            {
                return new NotFoundFileInfo(name);
            }
            return new EmbeddedResourceFileInfo(_assembly, resourcePath, name, _lastModified);
        }

        /// <summary>
        /// Enumerate a directory at the given path, if any.
        /// This file provider uses a flat directory structure. Everything under the base namespace is considered to be one directory.
        /// </summary>
        /// <param name="subpath">The path that identifies the directory</param>
        /// <returns>Contents of the directory. Caller must check Exists property.</returns>
        public IDirectoryContents GetDirectoryContents(string subpath)
        {
            // The file name is assumed to be the remainder of the resource name.
            if (subpath == null)
            {
                return new NotFoundDirectoryContents();
            }

            // Non-hierarchal.
            if (!subpath.Equals(string.Empty))
            {
                return new NotFoundDirectoryContents();
            }

            var entries = new List<IFileInfo>();

            // TODO: The list of resources in an assembly isn't going to change. Consider caching.
            var resources = _assembly.GetManifestResourceNames();
            for (var i = 0; i < resources.Length; i++)
            {
                var resourceName = resources[i];
                if (resourceName.StartsWith(_baseNamespace))
                {
                    entries.Add(new EmbeddedResourceFileInfo(
                        _assembly,
                        resourceName,
                        resourceName.Substring(_baseNamespace.Length),
                        _lastModified));
                }
            }

            return new EnumerableDirectoryContents(entries);
        }

        public IChangeToken Watch(string pattern)
        {
            return NoopChangeToken.Singleton;
        }

        private class EmbeddedResourceFileInfo : IFileInfo
        {
            private readonly Assembly _assembly;
            private readonly string _resourcePath;

            private long? _length;

            public EmbeddedResourceFileInfo(
                Assembly assembly,
                string resourcePath,
                string name,
                DateTimeOffset lastModified)
            {
                _assembly = assembly;
                _resourcePath = resourcePath;
                Name = name;
                LastModified = lastModified;
            }

            public bool Exists => true;

            public long Length
            {
                get
                {
                    if (!_length.HasValue)
                    {
                        using (var stream = _assembly.GetManifestResourceStream(_resourcePath))
                        {
                            _length = stream.Length;
                        }
                    }
                    return _length.Value;
                }
            }

            // Not directly accessible.
            public string PhysicalPath => null;

            public string Name { get; }

            public DateTimeOffset LastModified { get; }

            public bool IsDirectory => false;

            public Stream CreateReadStream()
            {
                var stream = _assembly.GetManifestResourceStream(_resourcePath);
                if (!_length.HasValue)
                {
                    _length = stream.Length;
                }
                return stream;
            }
        }
    }
}
