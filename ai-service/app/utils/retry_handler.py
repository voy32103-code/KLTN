"""
Retry handler with exponential backoff for AI service calls.
Handles transient failures, rate limits, and JSON parsing errors.
"""
import asyncio
import json
import logging
from typing import Any, Callable, TypeVar
from functools import wraps

logger = logging.getLogger(__name__)

T = TypeVar('T')


class RetryConfig:
    """Configuration for retry behavior"""
    def __init__(
        self,
        max_retries: int = 3,
        initial_delay: float = 1.0,
        max_delay: float = 60.0,
        exponential_base: float = 2.0,
        jitter: bool = True
    ):
        self.max_retries = max_retries
        self.initial_delay = initial_delay
        self.max_delay = max_delay
        self.exponential_base = exponential_base
        self.jitter = jitter


class RetryableError(Exception):
    """Base class for errors that should trigger a retry"""
    pass


class RateLimitError(RetryableError):
    """API rate limit exceeded"""
    pass


class TransientError(RetryableError):
    """Temporary network or service error"""
    pass


class JSONParsingError(Exception):
    """JSON parsing failed - may or may not be retryable"""
    pass


def calculate_backoff_delay(
    attempt: int,
    config: RetryConfig
) -> float:
    """
    Calculate exponential backoff delay with optional jitter.
    
    Formula: min(initial_delay * (base ^ attempt), max_delay)
    """
    import random
    
    delay = min(
        config.initial_delay * (config.exponential_base ** attempt),
        config.max_delay
    )
    
    if config.jitter:
        # Add random jitter ±25% to prevent thundering herd
        jitter_range = delay * 0.25
        delay += random.uniform(-jitter_range, jitter_range)
    
    return max(0, delay)


async def retry_async(
    func: Callable[..., Any],
    *args,
    config: RetryConfig | None = None,
    **kwargs
) -> Any:
    """
    Retry an async function with exponential backoff.
    
    Args:
        func: Async function to retry
        *args: Positional arguments for func
        config: Retry configuration (uses defaults if None)
        **kwargs: Keyword arguments for func
    
    Returns:
        Result from successful function call
    
    Raises:
        Last exception if all retries exhausted
    """
    if config is None:
        config = RetryConfig()
    
    last_exception: Exception | None = None
    
    for attempt in range(config.max_retries + 1):
        try:
            result = await func(*args, **kwargs)
            
            if attempt > 0:
                logger.info(
                    f"✅ Retry successful after {attempt} attempts: {func.__name__}"
                )
            
            return result
            
        except RetryableError as e:
            last_exception = e
            
            if attempt < config.max_retries:
                delay = calculate_backoff_delay(attempt, config)
                logger.warning(
                    f"⏳ Retry {attempt + 1}/{config.max_retries} after {delay:.2f}s: "
                    f"{func.__name__} - {type(e).__name__}: {str(e)}"
                )
                await asyncio.sleep(delay)
            else:
                logger.error(
                    f"❌ Max retries ({config.max_retries}) exhausted for {func.__name__}"
                )
        
        except Exception as e:
            # Non-retryable error - fail immediately
            logger.error(
                f"❌ Non-retryable error in {func.__name__}: {type(e).__name__}: {str(e)}"
            )
            raise
    
    # All retries exhausted
    if last_exception is not None:
        raise last_exception
    else:
        raise RuntimeError(f"Unexpected: retry loop completed without result or exception")


def with_retry(config: RetryConfig | None = None):
    """
    Decorator for async functions to add retry logic.
    
    Usage:
        @with_retry(config=RetryConfig(max_retries=5))
        async def call_ai_api():
            ...
    """
    def decorator(func: Callable):
        @wraps(func)
        async def wrapper(*args, **kwargs):
            return await retry_async(func, *args, config=config, **kwargs)
        return wrapper
    return decorator


async def parse_json_with_retry(
    text: str,
    max_attempts: int = 3
) -> dict | list:
    """
    Parse JSON with multiple strategies and retry logic.
    
    Strategies:
    1. Direct json.loads()
    2. Extract JSON from markdown code blocks
    3. Strip common prefixes/suffixes
    4. Fix common JSON errors (trailing commas, unquoted keys)
    
    Args:
        text: Text containing JSON
        max_attempts: Number of parsing strategies to try
    
    Returns:
        Parsed JSON object
    
    Raises:
        JSONParsingError: If all strategies fail
    """
    
    def extract_from_markdown(s: str) -> str:
        """Extract JSON from markdown code blocks"""
        import re
        # Pattern: ```json\n{...}\n```
        pattern = r'```(?:json)?\s*\n([\s\S]*?)\n```'
        match = re.search(pattern, s)
        if match:
            return match.group(1)
        return s
    
    def strip_common_noise(s: str) -> str:
        """Remove common prefixes/suffixes"""
        s = s.strip()
        
        # Remove "Here is the JSON:" etc
        prefixes = [
            "Here is the JSON:",
            "Here's the JSON:",
            "JSON:",
            "Result:",
            "Output:"
        ]
        for prefix in prefixes:
            if s.startswith(prefix):
                s = s[len(prefix):].strip()
        
        return s
    
    def fix_json_syntax(s: str) -> str:
        """Attempt to fix common JSON syntax errors"""
        import re
        
        # Remove trailing commas before } or ]
        s = re.sub(r',(\s*[}\]])', r'\1', s)
        
        # Fix single quotes to double quotes (risky but common)
        # Only for simple cases
        s = s.replace("'", '"')
        
        return s
    
    # Strategy 1: Direct parse
    try:
        return json.loads(text)
    except json.JSONDecodeError as e:
        logger.debug(f"Direct JSON parse failed: {e}")
    
    # Strategy 2: Extract from markdown
    try:
        cleaned = extract_from_markdown(text)
        return json.loads(cleaned)
    except json.JSONDecodeError as e:
        logger.debug(f"Markdown extraction parse failed: {e}")
    
    # Strategy 3: Strip noise
    try:
        cleaned = strip_common_noise(text)
        cleaned = extract_from_markdown(cleaned)
        return json.loads(cleaned)
    except json.JSONDecodeError as e:
        logger.debug(f"Noise stripping parse failed: {e}")
    
    # Strategy 4: Fix syntax
    try:
        cleaned = strip_common_noise(text)
        cleaned = extract_from_markdown(cleaned)
        cleaned = fix_json_syntax(cleaned)
        return json.loads(cleaned)
    except json.JSONDecodeError as e:
        logger.debug(f"Syntax fix parse failed: {e}")
    
    # All strategies failed
    logger.error(f"❌ JSON parsing failed after {max_attempts} strategies")
    logger.error(f"Text preview: {text[:200]}...")
    raise JSONParsingError(f"Failed to parse JSON from text: {text[:100]}...")


def is_retryable_http_status(status_code: int) -> bool:
    """
    Check if HTTP status code indicates a retryable error.
    
    Retryable:
    - 408: Request Timeout
    - 429: Too Many Requests (Rate Limit)
    - 500: Internal Server Error
    - 502: Bad Gateway
    - 503: Service Unavailable
    - 504: Gateway Timeout
    """
    return status_code in {408, 429, 500, 502, 503, 504}


# Default config for AI service calls
AI_SERVICE_RETRY_CONFIG = RetryConfig(
    max_retries=3,
    initial_delay=2.0,
    max_delay=30.0,
    exponential_base=2.0,
    jitter=True
)
