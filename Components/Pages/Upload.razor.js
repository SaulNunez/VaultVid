// Imported as a module by Upload.razor; it deliberately does not touch the global scope.
export function previewImage(inputElem, imgElem) {
    const file = inputElem.files?.[0];
    if (!file) {
        return;
    }

    const url = URL.createObjectURL(file);
    imgElem.addEventListener('load', () => URL.revokeObjectURL(url), { once: true });
    imgElem.src = url;
}
