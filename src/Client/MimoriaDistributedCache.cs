﻿// SPDX-FileCopyrightText: 2025 varelen
//
// SPDX-License-Identifier: MIT

using Microsoft.Extensions.Caching.Distributed;

namespace Varelen.Mimoria.Client;

public sealed class MimoriaDistributedCache : IDistributedCache
{
    private readonly IMimoriaClient mimoriaClient;

    public MimoriaDistributedCache(IMimoriaClient mimoriaClient)
        => this.mimoriaClient = mimoriaClient;

    public byte[]? Get(string key)
    {
        // TODO: Review how to properly wrap this. I mean it's better than .Result but still..
        return this.GetAsync(key).GetAwaiter().GetResult();
    }

    public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        => this.mimoriaClient.GetBytesAsync(key, token);

    public void Refresh(string key)
    {
        // TODO: Implement?
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        // TODO: Implement?
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        // TODO: Review how to properly wrap this. I mean it's better than .Result but still..
        this.RemoveAsync(key).GetAwaiter().GetResult();
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
        => this.mimoriaClient.DeleteAsync(key, token);

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        // TODO: Review how to properly wrap this. I mean it's better than .Result but still..
        this.SetAsync(key, value, options).GetAwaiter().GetResult();
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        => this.mimoriaClient.SetBytesAsync(key, value, options.SlidingExpiration ?? default, token);
}
