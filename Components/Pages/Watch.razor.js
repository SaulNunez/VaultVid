// Loaded as a module by Watch.razor via JS interop, so nothing runs until a video is ready.
import { VidstackPlayer, VidstackPlayerLayout } from 'https://cdn.vidstack.io/player';

export async function createPlayer(container, src, poster, title) {
    const player = await VidstackPlayer.create({
        target: container,
        title: title ?? '',
        src,
        poster: poster ?? undefined,
        layout: new VidstackPlayerLayout(),
    });

    // Wrapped so .NET gets a handle it can call destroy() on and dispose later.
    return DotNet.createJSObjectReference({
        destroy: () => player.destroy(),
    });
}
