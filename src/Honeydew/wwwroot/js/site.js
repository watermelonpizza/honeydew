function fallbackCopyTextToClipboard(text, onComplete, onError) {
  const textArea = document.createElement("textarea");
  textArea.value = text;

  // Avoid scrolling to bottom
  textArea.style.top = "0";
  textArea.style.left = "0";
  textArea.style.position = "fixed";

  document.body.appendChild(textArea);
  textArea.focus();
  textArea.select();

  try {
    const successful = document.execCommand('copy');

    if (successful) {
      onComplete && onComplete();
    } else {
      onError && onError('Copy to clipboard failed for unknown reasons');
    }
  } catch (err) {
    console.error('Failed to copy text', text, err);
    onError && onError(err);
  }

  document.body.removeChild(textArea);
}

function copyText(text, onComplete, onError) {
  if (!navigator.clipboard) {
    fallbackCopyTextToClipboard(text);
    return;
  }

  navigator.clipboard.writeText(text)
    .then(() => onComplete && onComplete())
    .catch(reason => {
      console.error('Failed to copy text', text, reason);

      onError && onError(reason)
    });
}


async function patch(url, data) {
  return fetch(url, {
    method: 'PATCH',
    headers: {
      'Content-Type': 'application/json'
    },
    body: JSON.stringify(data)
  });
}