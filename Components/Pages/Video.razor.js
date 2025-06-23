
import { VidstackPlayer, VidstackPlayerLayout } from 'https://cdn.vidstack.io/player';

const player = await VidstackPlayer.create({
    target: '#target',
    title: 'Sprite Fight',
    src: 'https://files.vidstack.io/sprite-fight/720p.mp4',
    poster: 'https://files.vidstack.io/sprite-fight/poster.webp',
    layout: new VidstackPlayerLayout({
        thumbnails: 'https://files.vidstack.io/sprite-fight/thumbnails.vtt',
    }),
});
