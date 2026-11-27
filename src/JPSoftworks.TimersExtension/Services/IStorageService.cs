// ------------------------------------------------------------
//
// Copyright (c) Jiří Polášek. All rights reserved.
//
// ------------------------------------------------------------

namespace JPSoftworks.TimersExtension.Services;

public interface IStorageService<T>
{
    IReadOnlyList<T> Load();

    void Save(IReadOnlyList<T> items);
}