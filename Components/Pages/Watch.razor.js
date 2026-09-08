// Loaded as a module by Watch.razor via JS interop, so nothing runs until a video is ready.
import { VidstackPlayer, VidstackPlayerLayout } from 'https://cdn.vidstack.io/player';

export async function createPlayer(container, src, poster, title) {
    // Ready videos are served as an HLS master playlist through /media; the type is stated
    // explicitly rather than left to extension sniffing.
    const source = src.includes('.m3u8')
        ? { src, type: 'application/x-mpegurl' }
        : src;

    const player = await VidstackPlayer.create({
        target: container,
        title: title ?? '',
        src: source,
        poster: poster ?? undefined,
        layout: new VidstackPlayerLayout(),
    });

    // Wrapped so .NET gets a handle it can call destroy() on and dispose later.
    return DotNet.createJSObjectReference({
        destroy: () => player.destroy(),
    });
}
